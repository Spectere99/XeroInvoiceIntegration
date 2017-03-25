using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using CsvHelper;
using SIMSData;
using Xero.Api.Core.Model;
using ValidationException = Xero.Api.Infrastructure.Exceptions.ValidationException;
using log4net;
using log4net.Util;
using Microsoft.Win32.SafeHandles;
using Xero.Api.Core.Model.Status;
using XeroInvoiceIntegration.DataObjects;

namespace XeroInvoiceIntegration
{
    class Program
    {

        private static ILog _log;
        private static int _transactionCount;
        private static DateTime _lastTime;
        private static TimeSpan _elapsedTimeSpan = TimeSpan.Zero;

        static void Main(string[] args)
        {
            //setup commandline Options
            var options = new Options();

            _lastTime = DateTime.Now;
            if (CommandLine.Parser.Default.ParseArguments(args, options))
            {
                //setup logger
                _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

                //setup audit file
                string auditLocationBase = ConfigurationManager.AppSettings["AuditFilesLocation"];

                string auditCustomerFile = Environment.CurrentDirectory + auditLocationBase +
                                           DateTime.Now.ToString("yyyyMMddhhmmss") + "_Customer.csv";
                string auditInvoiceFile = Environment.CurrentDirectory + auditLocationBase +
                                          DateTime.Now.ToString("yyyyMMddhhmmss") + "_Invoice.csv";
                string auditPaymentFile = Environment.CurrentDirectory + auditLocationBase +
                                          DateTime.Now.ToString("yyyyMMddhhmmss") + "_Payment.csv";
                string exceptionFile = Environment.CurrentDirectory + auditLocationBase +
                                          DateTime.Now.ToString("yyyyMMddhhmmss") + "_Exceptions.csv";

                if (!Directory.Exists(Environment.CurrentDirectory + @"\audit\"))
                {
                    Directory.CreateDirectory(Environment.CurrentDirectory + @"\audit\");
                }

                TextWriter customerAuditTextWriter = new StreamWriter(auditCustomerFile);
                TextWriter invoiceAuditTextWriter = new StreamWriter(auditInvoiceFile);
                TextWriter paymentAuditTextWriter = new StreamWriter(auditPaymentFile);
                TextWriter exceptionAuditTextWriter = new StreamWriter(exceptionFile);

                var customerCsv = new CsvWriter(customerAuditTextWriter);
                var invoiceCsv = new CsvWriter(invoiceAuditTextWriter);
                var paymentCsv = new CsvWriter(paymentAuditTextWriter);
                var exceptionCsv = new CsvWriter(exceptionAuditTextWriter);

                bool customerHeaderWritten = false;
                bool invoiceHeaderWritten = false;
                bool paymentHeaderWritten = false;
                bool exceptionHeaderWritten = false;


                bool dailyRun = (ConfigurationManager.AppSettings["DailyRun"] == "Y") ? true : false;
                bool emailResults = (ConfigurationManager.AppSettings["EmailResults"] == "Y") ? true : false;
                string[] emailTo = ConfigurationManager.AppSettings["ToEmail"].Split(',');
                var startDateConfig = ConfigurationManager.AppSettings["StartDate"];
                var paymentBackDate = DateTime.Parse(ConfigurationManager.AppSettings["PaymentBackDate"]);

                SIMSDataEntities dataEntities = new SIMSDataEntities();
                SIMSMapper simsMapper = new SIMSMapper();
                
                var now = DateTime.Now;
                var defaultStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                var configStartDate = DateTime.Parse(startDateConfig);

                DateTime selectDate = defaultStart;
                if (!dailyRun)
                {
                    selectDate = configStartDate;
                }
                DateTime startTime = DateTime.Now;
               
                _log.Info("Processing SIMS Data...");
                _log.Info(string.Format(" Run Date: {0}", DateTime.Now));
                _log.Info("***************** Parameters *********************");
                _log.Info(string.Format("** Daily Run: {0}", dailyRun));
                _log.Info(string.Format("** StartDate Config: {0}", startDateConfig));
                _log.Info(string.Format("** Configured Start Date: {0}", configStartDate));
                _log.Info(string.Format("** Select Date: {0}", selectDate));
                _log.Info(string.Format("** Transmit to XERO?: {0}", options.TransmitToXero));
                _log.Info(string.Format("** Customer Audit File: {0}", auditCustomerFile));
                _log.Info(string.Format("** Invoice Audit File: {0}", auditInvoiceFile));
                _log.Info(string.Format("** Payment Audit File: {0}", auditPaymentFile));
                _log.Info(string.Format("** Exception Audit File: {0}", exceptionFile));
                _log.Info("***************** End Parameters *********************");
                XeroIntegration xeroIntegration = new XeroIntegration(options.TransmitToXero);
                var dailyOrderNumbers = dataEntities.order_status_history.Where(p => p.order_status.Equals("com"))
                    .Where(o => o.status_date >= selectDate);
                _log.Info(string.Format("********** Processing {0} Daily Orders *************", dailyOrderNumbers.Count() ));
                
                foreach (var stat in dailyOrderNumbers)
                {
                    CustomerAudit customerAudit = new CustomerAudit();
                    InvoiceAudit invoiceAudit = new InvoiceAudit();
                    PaymentAudit paymentAudit = new PaymentAudit();
                    try
                    {
                        var orderIdSearch = int.Parse(stat.order_id);
                        Console.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
                        _log.Info(string.Format("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date));
                        //file.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
                        IEnumerable<order> orderHeaders = dataEntities.orders.Where(o => o.order_id == orderIdSearch);
                        foreach (var header in orderHeaders)
                        {
                            //Check on the Customer / Xero Contact
                            if (header.customer_id != null)
                            {
                                int customerId = int.Parse(header.customer_id.ToString());

                                var xeroContact = simsMapper.BuildContact(customerId);
                                customerAudit = new CustomerAudit();
                                if (!customerHeaderWritten)
                                {
                                    customerCsv.WriteHeader(customerAudit.GetType());
                                    customerHeaderWritten = true;
                                }

                                if (options.TransmitToXero)
                                {
                                    WaitCheck(1);
                                    xeroContact = xeroIntegration.CreateContact(xeroContact, options.TransmitToXero);
                                }

                                customerAudit.CustomerID = customerId;
                                customerAudit.CustomerName = xeroContact.Name;
                                customerAudit.Address = xeroContact.Addresses != null
                                    ? xeroContact.Addresses.FirstOrDefault().AddressLine1
                                    : string.Empty;
                                customerAudit.City = xeroContact.Addresses != null
                                    ? xeroContact.Addresses.FirstOrDefault().City
                                    : string.Empty;
                                customerAudit.State = xeroContact.Addresses != null
                                    ? xeroContact.Addresses.FirstOrDefault().Region
                                    : string.Empty;
                                customerAudit.Zip = xeroContact.Addresses != null
                                    ? xeroContact.Addresses.FirstOrDefault().PostalCode
                                    : string.Empty;
                                customerAudit.ContactEmail = xeroContact.EmailAddress ?? string.Empty;
                                if (xeroContact.ContactPersons != null)
                                {
                                    if (xeroContact.ContactPersons.Count != 0)
                                    {
                                        customerAudit.ContactName = string.Format("{0} {1}",
                                            xeroContact.ContactPersons.FirstOrDefault().FirstName,
                                            xeroContact.ContactPersons.FirstOrDefault().LastName);
                                    }
                                }
                                if (xeroContact.Phones != null)
                                {
                                    if (xeroContact.Phones != null || xeroContact.Phones.Count != 0)
                                    {
                                        customerAudit.ContactPhone = string.Format("({0}){1}",
                                            xeroContact.Phones.FirstOrDefault().PhoneAreaCode,
                                            xeroContact.Phones.FirstOrDefault().PhoneNumber);
                                    }
                                }
                                customerAudit.Email = xeroContact.EmailAddress;
                                customerCsv.WriteRecord(customerAudit);

                                string orderid = header.order_id.ToString();
                                order_status_history statusHistory =
                                    dataEntities.order_status_history.Where(o => o.order_id == orderid)
                                        .FirstOrDefault(d => d.order_status.Equals("com"));
                                user assignedTo =
                                    dataEntities.users.FirstOrDefault(o => o.user_id == header.assigned_user_id);

                                DateTime completeDate = DateTime.Parse(statusHistory.status_date.ToString());
                                string referenceNumber = assignedTo.first_name.Substring(0, 1).ToUpper() +
                                                         assignedTo.last_name.Substring(0, 1).ToUpper() + " " +
                                                         header.order_number;
                                //Build Invoice
                                if (header.xero_invoice_id == null)
                                {
                                    var xeroInvoice = simsMapper.BuildInvoice(header, completeDate, referenceNumber,
                                        xeroContact);
                                    invoiceAudit = new InvoiceAudit();
                                    if (!invoiceHeaderWritten)
                                    {
                                        invoiceCsv.WriteHeader(invoiceAudit.GetType());
                                        invoiceHeaderWritten = true;
                                    }

                                    // Create the Invoice
                                    if (options.TransmitToXero)
                                    {
                                        WaitCheck(1);
                                        var simsInvoiceTotal = xeroInvoice.AmountDue;

                                        xeroInvoice = xeroIntegration.CreateInvoice(xeroInvoice, options.TransmitToXero);
                                        decimal? invoiceDiff = xeroInvoice.AmountDue - simsInvoiceTotal;
                                        if (invoiceDiff != (decimal?) 0.0)
                                        {
                                            xeroIntegration.DeleteInvoice(xeroInvoice, options.TransmitToXero);
                                            xeroInvoice.LineItems.Add(
                                                simsMapper.BuildSalesTaxAdjustmentLineItem(invoiceDiff));
                                            xeroInvoice.Status = InvoiceStatus.Draft;
                                            xeroInvoice.Id = Guid.Empty;
                                            xeroInvoice = xeroIntegration.CreateInvoice(xeroInvoice,
                                                options.TransmitToXero);
                                        }
                                        if (xeroInvoice.Id != Guid.Empty)
                                        {
                                            int nextPk =
                                                dataEntities.order_status_history.OrderByDescending(
                                                        p => p.order_status_history_id)
                                                    .FirstOrDefault()
                                                    .order_status_history_id +
                                                1;
                                            // Update SIMS order_status_history table.
                                            order_status_history orderStatus = new order_status_history();
                                            orderStatus.order_status_history_id = nextPk;
                                            orderStatus.order_id = header.order_id.ToString();
                                            orderStatus.order_status = "inst";
                                            orderStatus.status_date = DateTime.Now;

                                            //dataEntities.Database.Log = s => Debug.WriteLine(s);

                                            dataEntities.order_status_history.Add(orderStatus);
                                            dataEntities.SaveChanges();

                                            //Check and see if 0 balance.   If so, update status to 'clos' (closed)
                                            if (double.Parse(header.balance_due) == 0.0)
                                            {
                                                int clsdPk = dataEntities.order_status_history.OrderByDescending(
                                                                     p => p.order_status_history_id)
                                                                 .FirstOrDefault()
                                                                 .order_status_history_id +
                                                             1;
                                                order_status_history orderClosedStatus = new order_status_history();
                                                orderClosedStatus.order_status_history_id = clsdPk;
                                                orderClosedStatus.order_id = header.order_id.ToString();
                                                orderClosedStatus.order_status = "clos";
                                                orderClosedStatus.status_date = DateTime.Now;

                                                dataEntities.order_status_history.Add(orderClosedStatus);
                                                dataEntities.SaveChanges();
                                            }

                                        }
                                        var order = from ord in dataEntities.orders
                                            where ord.order_id == header.order_id
                                            select ord;

                                        order updOrder = order.Single();
                                        updOrder.xero_invoice_id = xeroInvoice.Id.ToString();

                                        dataEntities.SaveChanges();
                                    }

                                    invoiceAudit.CreateDate = xeroInvoice.Date;
                                    invoiceAudit.InvoiceAmt = xeroInvoice.AmountDue;
                                    invoiceAudit.InvoiceDueDate = xeroInvoice.DueDate;
                                    invoiceAudit.LineItemCount = xeroInvoice.LineItems.Count;
                                    invoiceAudit.OrderId = header.order_id;
                                    invoiceAudit.OrderNumber = header.order_number;
                                    invoiceAudit.ReferenceNbr = xeroInvoice.Reference;
                                    invoiceAudit.XeroInvoiceId = xeroInvoice.Id.ToString();

                                    invoiceCsv.WriteRecord(invoiceAudit);
                                }
                            }
                        }
                    }
                    catch (ValidationException valEx)
                    {
                        var st = new StackTrace(valEx, true);
                        var frame = st.GetFrame(0);
                        var line = frame.GetFileLineNumber();
                        ExceptionAudit exceptionAudit = LogExceptionData(valEx, customerAudit, invoiceAudit, paymentAudit);
                        if (!exceptionHeaderWritten)
                        {
                            exceptionCsv.WriteHeader(exceptionAudit.GetType());
                            exceptionHeaderWritten = true;
                        }
                        exceptionCsv.WriteRecord(exceptionAudit);
                        _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line, valEx.Message);
                        _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(valEx));
                    }
                    catch (Exception ex)
                    {
                        var st = new StackTrace(ex, true);
                        var frame = st.GetFrame(0);
                        var line = frame.GetFileLineNumber();
                        ExceptionAudit exceptionAudit = LogExceptionData(ex, customerAudit, invoiceAudit, paymentAudit);
                        if (!exceptionHeaderWritten)
                        {
                            exceptionCsv.WriteHeader(exceptionAudit.GetType());
                            exceptionHeaderWritten = true;
                        }
                        exceptionCsv.WriteRecord(exceptionAudit);
                        _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line, ex.Message);
                        _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(ex));
                    }

                }
                //RWF new ADD 2/28/2017
                //Check for payments on previous invoices.
                if (options.TransmitToXero)
                {
                    //TODO:
                    //Exclude Today's date because we cannot apply a payment to an invoice that has not been approved.
                    var emptyGuid = Guid.Empty.ToString();
                    var pastOrderPaymentsNotProcessed = dataEntities.order_payments.Where(p => p.xero_payment_id == null || p.xero_payment_id == emptyGuid).ToList();
                    var pastOrderPaymentsDated = pastOrderPaymentsNotProcessed.Where(o => o.payment_date >= paymentBackDate).ToList();
                    var pastOrderPayments = pastOrderPaymentsDated.Where(q=>q.payment_date != DateTime.Now).ToList();

                    Console.WriteLine("  **** Processing Payments for Past Invoices ****");
                    Console.WriteLine(string.Format("Processing {0} Past Payments", pastOrderPayments.Count()));
                    _log.Info("  **** Processing Payments for Past Invoices ****");
                    _log.Info(string.Format("Processing {0} Past Payments", pastOrderPayments.Count()));

                    foreach (order_payments pastPayment in pastOrderPayments)
                    {
                        PaymentAudit paymentAudit = new PaymentAudit();
                        try
                        {
                            var pastOrder = dataEntities.orders.SingleOrDefault(p => p.order_id == pastPayment.order_id);
                            string orderId = pastOrder.order_id.ToString();
                            if (pastOrder != null)
                            {
                                user assignedTo =
                                    dataEntities.users.FirstOrDefault(o => o.user_id == pastOrder.assigned_user_id);

                                string referenceNumber = assignedTo.first_name.Substring(0, 1).ToUpper() +
                                                         assignedTo.last_name.Substring(0, 1).ToUpper() + " " +
                                                         pastOrder.order_number;
                                //Check order to see if it is closed.
                                //CAN'T DO THIS BECAUSE OF MULTIPLE PAYMENTS ON THE SAME DAY THAT MAKE THE INVOICE BALANCE DUE = $0.00.  NEED TO FIGURE BETTER WAY.

                                var foundOrderStatus = dataEntities.order_status_history.SingleOrDefault(p => p.order_id == orderId && p.order_status == "clos");
                                if (foundOrderStatus == null)
                                {
                                    // Check to see if it is in invoiced status
                                    foundOrderStatus =
                                        dataEntities.order_status_history.SingleOrDefault(
                                            p => p.order_id == orderId && p.order_status == "inst");

                                    if (foundOrderStatus != null)
                                    {
                                        Invoice matchInvoice = xeroIntegration.FindInvoiceDirect(referenceNumber);
                                        if (matchInvoice != null)
                                        {
                                            if (matchInvoice.Status != InvoiceStatus.Paid)
                                            {
                                                bool foundMatchedPayment = false;
                                                //Check to see if the payment is a prepayment.
                                                var prePayment =
                                                    matchInvoice.Prepayments.SingleOrDefault(
                                                        p => p.Total == decimal.Parse(pastPayment.payment_amount) && p.Date == pastPayment.payment_date);
                                                if (prePayment != null) //We found a prepayment
                                                {
                                                    //Update the Sims payment with the pre-payment ID
                                                    Common.UpdateSIMSPaymentComplete(prePayment.Id, pastPayment);
                                                    //var pymt = from pay in dataEntities.order_payments
                                                    //           where pay.order_payment_id == pastPayment.order_payment_id
                                                    //           select pay;

                                                    //order_payments updPayment = pymt.Single();

                                                    //updPayment.xero_payment_id = prePayment.Id.ToString();

                                                    //dataEntities.SaveChanges();
                                                    foundMatchedPayment = true;
                                                }

                                                var existingPayment =
                                                    matchInvoice.Payments.SingleOrDefault(
                                                        p => p.Amount == decimal.Parse(pastPayment.payment_amount) && p.Date == pastPayment.payment_date);
                                                if (existingPayment != null) //We found a prepayment
                                                {
                                                    //Update the Sims payment with the pre-payment ID
                                                    Common.UpdateSIMSPaymentComplete(existingPayment.Id, pastPayment);

                                                    //var pymt = from pay in dataEntities.order_payments
                                                    //           where pay.order_payment_id == pastPayment.order_payment_id
                                                    //           select pay;

                                                    //order_payments updPayment = pymt.Single();

                                                    //updPayment.xero_payment_id = existingPayment.Id.ToString();

                                                    //dataEntities.SaveChanges();
                                                    foundMatchedPayment = true;
                                                }

                                                if (!foundMatchedPayment)
                                                {
                                                    //Apply the payment.
                                                    Payment xeroPayment = simsMapper.BuildPayment(pastPayment,
                                                        matchInvoice);
                                                    paymentAudit = new PaymentAudit();
                                                    if (!paymentHeaderWritten)
                                                    {
                                                        paymentCsv.WriteHeader(paymentAudit.GetType());
                                                        paymentHeaderWritten = true;
                                                    }


                                                    if (options.TransmitToXero)
                                                    {
                                                        if (pastPayment.payment_type_code != "oth")
                                                        {
                                                            WaitCheck(1);
                                                            xeroPayment = xeroIntegration.CreatePayment(xeroPayment,
                                                                options.TransmitToXero); 
                                                        }
                                                        else
                                                        {
                                                            xeroPayment.Id =
                                                                new Guid("99999999-9999-9999-9999-999999999999");
                                                        }
                                                        Common.UpdateSIMSPaymentComplete(xeroPayment.Id, pastPayment);
                                                        //var pymt = from pay in dataEntities.order_payments
                                                        //        where
                                                        //        pay.order_payment_id == pastPayment.order_payment_id
                                                        //        select pay;
                                                        //order_payments updPayment = pymt.Single();

                                                        //updPayment.xero_payment_id = xeroPayment.Id.ToString();

                                                        //dataEntities.SaveChanges();

                                                        paymentAudit.CheckNumber = pastPayment.check_number;
                                                        paymentAudit.OrderId = pastOrder.order_id;
                                                        paymentAudit.OrderNumber = pastOrder.order_number;
                                                        paymentAudit.PaymentAmount = pastPayment.payment_amount;
                                                        paymentAudit.PaymentDate = pastPayment.payment_date;
                                                        paymentAudit.PaymentID = pastPayment.order_payment_id;
                                                        paymentAudit.PaymentType = pastPayment.payment_type_code;
                                                        paymentAudit.XeroPaymentId = xeroPayment.Id.ToString();

                                                        paymentCsv.WriteRecord(paymentAudit);
                                                    }
                                                    
                                                }
                                                //RWF TODO- NEED TO FIGURE BETTER WAY. SEE ABOVE NOTE ABOUT MULTI-PAYMENTS ON SAME DAY.
                                                //Check and see if 0 balance.   If so, update status to 'clos' (closed)
                                                if (double.Parse(pastOrder.balance_due) == 0.0)
                                                {
                                                    Common.CloseOrder(pastOrder.order_id);
                                                    //int clsdPk = dataEntities.order_status_history.OrderByDescending(
                                                    //                     p => p.order_status_history_id)
                                                    //                 .FirstOrDefault()
                                                    //                 .order_status_history_id +
                                                    //             1;
                                                    //order_status_history orderClosedStatus = new order_status_history();
                                                    //orderClosedStatus.order_status_history_id = clsdPk;
                                                    //orderClosedStatus.order_id = pastOrder.order_id.ToString();
                                                    //orderClosedStatus.order_status = "clos";
                                                    //orderClosedStatus.status_date = DateTime.Now;

                                                    //dataEntities.order_status_history.Add(orderClosedStatus);
                                                    //dataEntities.SaveChanges();
                                                }
                                            }
                                            else //This inovice was paid, so we need to do some updating.
                                            {
                                                //Update the Payment with the Xero Payment ID
                                                Common.UpdateSIMSPaymentComplete(matchInvoice, pastPayment);

                                                //var payment =
                                                //    matchInvoice.Payments.SingleOrDefault(
                                                //        p => p.Amount == decimal.Parse(pastPayment.payment_amount));
                                                //if (payment != null)
                                                //{
                                                //    var pymt = from pay in dataEntities.order_payments
                                                //        where pay.order_payment_id == pastPayment.order_payment_id
                                                //        select pay;

                                                //    order_payments updPayment = pymt.Single();

                                                //    updPayment.xero_payment_id = payment.Id.ToString();

                                                //    dataEntities.SaveChanges();
                                                //}

                                                

                                                // Now update the status of the SIMS invoice to closed.
                                                Common.CloseOrder(pastOrder.order_id);
                                                //int clsdPk = dataEntities.order_status_history.OrderByDescending(
                                                //                             p => p.order_status_history_id)
                                                //                         .FirstOrDefault()
                                                //                         .order_status_history_id +
                                                //                     1;
                                                //order_status_history orderClosedStatus = new order_status_history();
                                                //orderClosedStatus.order_status_history_id = clsdPk;
                                                //orderClosedStatus.order_id = pastOrder.order_id.ToString();
                                                //orderClosedStatus.order_status = "clos";
                                                //orderClosedStatus.status_date = DateTime.Now;

                                                //dataEntities.order_status_history.Add(orderClosedStatus);
                                                //dataEntities.SaveChanges();
                                            }
                                        }
                                    }
                                    
                                }
                            }
                        }
                        catch (ValidationException valEx)
                        {
                            var st = new StackTrace(valEx, true);
                            var frame = st.GetFrame(0);
                            var line = frame.GetFileLineNumber();
                            ExceptionAudit exceptionAudit = LogExceptionData(valEx, null, null, paymentAudit);
                            if (!exceptionHeaderWritten)
                            {
                                exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                exceptionHeaderWritten = true;
                            }
                            exceptionCsv.WriteRecord(exceptionAudit);
                            _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line, valEx.Message);
                            _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(valEx));
                        }
                        catch (Exception ex)
                        {
                            var st = new StackTrace(ex, true);
                            var frame = st.GetFrame(0);
                            var line = frame.GetFileLineNumber();
                            ExceptionAudit exceptionAudit = LogExceptionData(ex, null, null, paymentAudit);
                            if (!exceptionHeaderWritten)
                            {
                                exceptionCsv.WriteHeader(exceptionAudit.GetType());
                                exceptionHeaderWritten = true;
                            }
                            exceptionCsv.WriteRecord(exceptionAudit);
                            _log.ErrorFormat("An Error occurred when processing Orders: Line: {0} : {1}", line, ex.Message);
                            _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(ex));
                        }
                    }

                }
                else
                {
                    _log.Info("*****  Not Transmitting to Xero.  Skipping Check for Back Payments ********");
                }
                customerAuditTextWriter.Close();
                invoiceAuditTextWriter.Close();
                paymentAuditTextWriter.Close();
                exceptionAuditTextWriter.Close();
                _log.Info("***** Sending Daily Results Email *****");
                if (emailResults)
                {
                    SendCompleteEmail(emailTo, auditCustomerFile, auditInvoiceFile, auditPaymentFile, exceptionFile);
                }
                TimeSpan elapsedTime = DateTime.Now.Subtract(startTime);
                
                _log.Info("*******  Process Completed ********");
                _log.Info(string.Format("** Elapsed Time: {0:c}", elapsedTime));
                _log.Info("*************************************************");

            }
            
            //Console.ReadKey();
        }

        static void WaitCheck(int transCount)
        {
            _transactionCount += transCount;

            _elapsedTimeSpan = DateTime.Now.Subtract(_lastTime);
            _lastTime = DateTime.Now;
            if (_elapsedTimeSpan.Seconds < 60) // We need to check about waiting.
            {
                if (_transactionCount >= 60)
                {
                    _transactionCount = 0;
                    //Need to wait.
                    int waitTime = 60 - _elapsedTimeSpan.Seconds;
                    Thread.Sleep(waitTime * 1000);
                }
            }
        }

        private static void SendCompleteEmail(string[] emailTo, string customerAttch, string invoiceAttch, string paymentAttch, string exceptionAttch)
        {
            //var callbackUrl = Url.Action("ConvertReservation", "Registration", new { userId = user.Person.PersonId, code = user.RegistrationCode }, protocol: Request.Url.Scheme);
            string primaryEmail = emailTo[0];
            MailMessage m = new MailMessage(
            new MailAddress("rflowers@saber98.com", "Xero Integration"),
            new MailAddress(primaryEmail));
            foreach (string email in emailTo.Skip(1))
            {
                m.To.Add(email);    
            }
            m.Subject = "Xero Integration Complete";
            m.Body = string.Format("Nightly Xero Integration has completed.  Attached are the nightly files.");

            m.Attachments.Add(new System.Net.Mail.Attachment(customerAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(invoiceAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(paymentAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(exceptionAttch));

            m.IsBodyHtml = true;
            SmtpClient smtp = new SmtpClient("mail.saber98.com");
            smtp.UseDefaultCredentials = false;
            smtp.Credentials = new System.Net.NetworkCredential("rflowers@saber98.com", "Sp3ct3r399");

            smtp.EnableSsl = false;
            smtp.Send(m);
        }

        private static ExceptionAudit LogExceptionData(Exception ex, CustomerAudit customerAudit, InvoiceAudit invoiceAudit,
            PaymentAudit paymentAudit)
        {
            ExceptionAudit exceptionAudit = new ExceptionAudit();
            if (invoiceAudit != null)
            {
                exceptionAudit.OrderId = invoiceAudit.OrderId.ToString();
                exceptionAudit.OrderNumber = invoiceAudit.OrderNumber;
                exceptionAudit.InvoiceCreateDate = invoiceAudit.CreateDate;
                exceptionAudit.InvoiceDueDate = invoiceAudit.InvoiceDueDate;
                exceptionAudit.LineItemCount = invoiceAudit.LineItemCount;
                exceptionAudit.ReferenceNbr = invoiceAudit.ReferenceNbr;
                exceptionAudit.InvoiceAmt = invoiceAudit.InvoiceAmt;
                exceptionAudit.XeroInvoiceId = invoiceAudit.XeroInvoiceId;
            }
            if (customerAudit != null)
            {
                exceptionAudit.CustomerID = customerAudit.CustomerID;
                exceptionAudit.XeroCustomerID = customerAudit.XeroCustomerID;
                exceptionAudit.CustomerName = customerAudit.CustomerName;
                exceptionAudit.Email = customerAudit.Email;
                exceptionAudit.Address = customerAudit.Address;
                exceptionAudit.City = customerAudit.City;
                exceptionAudit.State = customerAudit.State;
                exceptionAudit.Zip = customerAudit.Zip;
                exceptionAudit.ContactName = customerAudit.ContactName;
                exceptionAudit.ContactEmail = customerAudit.ContactEmail;
                exceptionAudit.ContactPhone = customerAudit.ContactPhone;
            }
            if (paymentAudit != null)
            {
                exceptionAudit.OrderId = paymentAudit.OrderId.ToString();
                exceptionAudit.OrderNumber = paymentAudit.OrderNumber;
                exceptionAudit.PaymentID  = paymentAudit.PaymentID ;
                exceptionAudit.XeroPaymentId  = paymentAudit.XeroPaymentId ;
                exceptionAudit.PaymentDate  = paymentAudit.PaymentDate ;
                exceptionAudit.PaymentType  = paymentAudit.PaymentType ;
                exceptionAudit.CheckNumber  = paymentAudit.CheckNumber ;
                exceptionAudit.PaymentAmount  = paymentAudit.PaymentAmount ;
            }

            exceptionAudit.ExceptionMessage = Utilities.FlattenException(ex);

            return exceptionAudit;
        }
    }
}
