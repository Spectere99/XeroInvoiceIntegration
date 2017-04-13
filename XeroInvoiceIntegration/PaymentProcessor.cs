using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using log4net;
using SIMSData;
using Xero.Api.Core.Model;
using Xero.Api.Core.Model.Status;
using XeroInvoiceIntegration.DataObjects;

namespace XeroInvoiceIntegration
{
    public class PaymentProcessor
    {
        private static ILog _log;

        public List<GeneratedPayment> GeneratedXeroPayments { get; set; }
        public List<ExceptionAudit> PaymentExceptions { get; set; }

        private  XeroIntegration _xeroIntegration;

        private readonly List<Invoice> _xeroInvoices;

        private SIMSDataEntities _db;
        /*  
         * 1) Loop Through Xero Invoices
         * 2) For each Invoice Get The Order Number By Parsing out the Reference Number
         * 3) Get the order from the SIMS order table using the OrderNumber
         * 4) 
         */

        public PaymentProcessor(List<Invoice> xeroInvoices)
        {
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
            _xeroIntegration = new XeroIntegration();

            _db = new SIMSDataEntities();
            
            GeneratedXeroPayments = new List<GeneratedPayment>();
            _xeroInvoices = xeroInvoices;
        }

        public int GeneratePayments(DateTime searchStartDate)
        {
            if (_xeroInvoices.Count == 0)
            {
                throw new Exception("There are no invoices to process.  Check that Invoices were pulled from Xero.");
            }
            foreach (Invoice invoice in _xeroInvoices)
            {
                StringBuilder sbPaymentChecks = new StringBuilder();
                if (invoice.Status != InvoiceStatus.Paid)
                {
                    sbPaymentChecks.AppendLine(string.Format("Invoice: {0} Not Paid", invoice.Id));
                    _log.InfoFormat("**** Invoice: {0} - Not Paid", invoice.Id);
                    if (invoice.Status == InvoiceStatus.Authorised)
                    {
                        sbPaymentChecks.AppendLine(string.Format("Invoice Status: {0}", invoice.Status));
                        _log.Info("*** Invoice is Authorized");
                        try
                        {
                            _log.InfoFormat("*** Invoice Reference Number: {0}", invoice.Reference);
                            var orderNumber = ParseOrderNumber(invoice.Reference);
                            _log.InfoFormat("*** Checking in SIMS for Payments to Order Number {0}", orderNumber);
                            var simsOrder = _db.orders.SingleOrDefault(p => p.order_number == orderNumber);
                            if (simsOrder != null)
                            {
                                var simsPayments = _db.order_payments.Where(p => p.order_id == simsOrder.order_id)
                                                                     .Where(p => p.payment_date >= searchStartDate).ToList();
                                sbPaymentChecks.AppendLine(string.Format("*** Found {0} Payments in SIMS since {1}", simsPayments.Count, searchStartDate));
                                _log.InfoFormat("*** Found {0} Payments in SIMS", simsPayments.Count);
                                // Need to matchup the Invoice's PrePayments with any payment records from SIMS, 
                                // and Match it to the Payment Integration Control table.  
                                if (invoice.Prepayments != null)
                                {
                                    simsPayments = ProcessPrePayments(simsPayments, invoice, orderNumber);
                                }
                                if (invoice.Payments != null)
                                {
                                    simsPayments = ProcessPayments(simsPayments, invoice, orderNumber);
                                }
                                SIMSMapper simsMapper = new SIMSMapper();
                                sbPaymentChecks.AppendLine(string.Format("*** {0} Payments to send to Xero", simsPayments.Count));
                                //Now the only payments left should be the ones that we need to process (ie. send back to main control program).
                                foreach (order_payments orderPayment in simsPayments)
                                {
                                    StringBuilder sbPayment = new StringBuilder();
                                    sbPayment.AppendLine(sbPaymentChecks.ToString());

                                    if (orderPayment.payment_type_code != "oth")
                                    {
                                        GeneratedPayment genPayment = new GeneratedPayment();
                                        genPayment.Payment = simsMapper.BuildPayment(orderPayment, invoice);
                                        
                                        PaymentAudit paymentAudit = new PaymentAudit();
                                        paymentAudit.CheckNumber = orderPayment.check_number;
                                        paymentAudit.OrderId = simsOrder.order_id;
                                        paymentAudit.OrderNumber = simsOrder.order_number;
                                        paymentAudit.PaymentAmount = orderPayment.payment_amount;
                                        paymentAudit.PaymentDate = orderPayment.payment_date;
                                        paymentAudit.PaymentID = orderPayment.order_payment_id;
                                        paymentAudit.PaymentType = orderPayment.payment_type_code;
                                        paymentAudit.Action = sbPayment.ToString();

                                        genPayment.PaymentAudit = paymentAudit;

                                        GeneratedXeroPayments.Add(genPayment);
                                    }
                                }
                            }
                            else
                            {
                                _log.InfoFormat("*** SIMS Order not found for Order Number: {0}", orderNumber);
                            }


                        }
                        catch (Exception ex)
                        {
                            var st = new StackTrace(ex, true);
                            var frame = st.GetFrame(0);
                            var line = frame.GetFileLineNumber();

                            _log.ErrorFormat("!!!! An Error occurred when processing Orders: Line: {0} : {1}", line,
                                    Utilities.FlattenException(ex));
                        }    
                    }
                    else
                    {
                        _log.Info("*** Invoice is NOT Authorized");
                    }

                }
            }
            return GeneratedXeroPayments.Count;
        }

        private List<order_payments> ProcessPrePayments(List<order_payments> simsPayments, Invoice invoice, string orderNumber )
        {
            foreach (Prepayment prePayment in invoice.Prepayments)
            {
                Prepayment fullPrePayment =
                    _xeroIntegration.FindPrepaymentByIdDirect(prePayment.Id.ToString());

                if (fullPrePayment != null)
                {
                    _log.Info("*** Checking PrePayment Allocations");
                    //See if the payment is in the payment_integration_control table.
                    var strPaymentId = fullPrePayment.Id.ToString();
                    var processedPayment =
                        _db.payment_interface_control.SingleOrDefault(p => p.xero_payment_id == strPaymentId);
                    if (processedPayment != null)
                    {
                        //Xero Sees it remove it from the list of Payments pulled for the order in SIMS
                        //Remove it from the simsPayment list.
                        _log.Info(string.Format("*** Found Payment {0} in control table", fullPrePayment.Id));
                        simsPayments.RemoveAll(p => p.order_payment_id == processedPayment.order_payment_id);
                    }
                    else
                    {  //Couldn't find a processed record, but must find the a payment and match it.
                        // Matching a payment can be difficult because an invoice can have multiple payments that have the same date
                        // and same amount.
                        var pass1Payments =
                            simsPayments.Where(p => (p.payment_date == fullPrePayment.Date)).ToList();
                        _log.Info(string.Format("*** Found {0} Payments made on {1}", pass1Payments.Count, fullPrePayment.Date));
                        foreach (order_payments p1Payment in pass1Payments)
                        {
                            //Need to check the allocations for PrePayments in order to not double send for prepayments in Xero.
                            var foundMatchingAllocation = false;
                            PrepaymentAllocation prePaymentAllocation = null;
                            foreach (PrepaymentAllocation alloc in fullPrePayment.Allocations)
                            {
                                if (Decimal.Parse(p1Payment.payment_amount) != alloc.Amount ||
                                    invoice.Id != alloc.Invoice.Id) continue;
                                prePaymentAllocation = alloc;
                                foundMatchingAllocation = true;
                                break;
                            }
                            //if (Decimal.Parse(p1Payment.payment_amount) != fullPrePayment.Total)continue;
                            if (!foundMatchingAllocation) continue;

                            _log.Info(string.Format("*** Payment Match amounts ${0}(Xero) / ${1}(Sims)", prePaymentAllocation.Amount, p1Payment.payment_amount)); 
                            _log.Info("*** Payment not in control, but in Xero.  Updating Payment Control table.");
                            simsPayments.RemoveAll(p => p.order_payment_id == p1Payment.order_payment_id);
                            //We have a payment. Update the payment_interface_control so it won't show up next time.
                            Common.RecordXeroPaymentControl(orderNumber, p1Payment, fullPrePayment.Id.ToString());
                            //Exit out of the foreach loop since we found the first matching payment.
                            break;
                        }
                    }

                }
            }
            return simsPayments;
        }

        private List<order_payments> ProcessPayments(List<order_payments> simsPayments, Invoice invoice, string orderNumber)
        {
            foreach (Payment payment in invoice.Payments)
            {
                Payment fullPayment =
                    _xeroIntegration.FindPaymenteByIdDirect(payment.Id.ToString());

                if (fullPayment != null)
                {
                    _log.Info("*** Checking Payment Allocations");
                    //See if the payment is in the payment_integration_control table.
                    var strPaymentId = fullPayment.Id.ToString();
                    var processedPayment =
                        _db.payment_interface_control.SingleOrDefault(p => p.xero_payment_id == strPaymentId);
                    if (processedPayment != null)
                    {
                        //Xero Sees it remove it from the list of Payments pulled for the order in SIMS
                        //Remove it from the simsPayment list.
                        _log.Info(string.Format("*** Found Payment {0} in control table", fullPayment.Id));
                        simsPayments.RemoveAll(p => p.order_payment_id == processedPayment.order_payment_id);
                    }
                    else
                    {  //Couldn't find a processed record, but must find the a payment and match it.
                        // Matching a payment can be difficult because an invoice can have multiple payments that have the same date
                        // and same amount.
                        var pass1Payments =
                            simsPayments.Where(p => (p.payment_date == fullPayment.Date)).ToList();
                        _log.Info(string.Format("*** Found {0} Payments made on {1}", pass1Payments.Count, fullPayment.Date));
                        foreach (order_payments p1Payment in pass1Payments)
                        {
                            if (Decimal.Parse(p1Payment.payment_amount) != fullPayment.Amount)
                                continue;

                            _log.Info(string.Format("*** Payment Match amounts ${0}(Xero) / ${1}(Sims)", fullPayment.Amount, p1Payment.payment_amount));
                            _log.Info("*** Payment not in control, but in Xero.  Updating Payment Control table.");
                            simsPayments.RemoveAll(p => p.order_payment_id == p1Payment.order_payment_id);
                            //We have a payment. Update the payment_interface_control so it won't show up next time.
                            Common.RecordXeroPaymentControl(orderNumber, p1Payment, fullPayment.Id.ToString());
                            //Exit out of the foreach loop since we found the first matching payment.
                            break;
                        }
                    }

                }
            }
            return simsPayments;
        }
        private string ParseOrderNumber(string xeroInvoiceRefNumber)
        {
            string orderNumber = string.Empty;
            if (xeroInvoiceRefNumber.Trim().Length > 0)
            {
                //Check to see if the first character is an alpha character.  
                if (Char.IsLetter(xeroInvoiceRefNumber, 0))
                {
                    //  If so, then we should have a valid reference number
                    //      See if the 3rd character is a space.
                    //      If so, then take from 4th character to end for order number
                    //      If not, then take 3rd character to end for order number.
                    orderNumber = xeroInvoiceRefNumber.Substring(2, 1) == " "
                        ? xeroInvoiceRefNumber.Substring(3)
                        : xeroInvoiceRefNumber.Substring(2);
                }
                else
                {
                    //  If not alpha character, then throw error about an invalid RefNumber (Log it too!).
                    throw new Exception(string.Format("Invoice Ref Number {0} is not valid", xeroInvoiceRefNumber));
                }

                return orderNumber;
            }
            throw new Exception(string.Format("Invoice Ref Number {0} is not valid", xeroInvoiceRefNumber));
        }
    }
}
