using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using SIMSData;
using Xero.Api.Core.Model;
using Xero.Api.Core.Model.Types;
using ValidationException = Xero.Api.Infrastructure.Exceptions.ValidationException;
using log4net;

namespace XeroInvoiceIntegration
{
    class Program
    {

        private static log4net.ILog _log = null;

        static void Main(string[] args)
        {
            //setup commandline Options
            var options = new Options();

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

                if (!Directory.Exists(Environment.CurrentDirectory + @"\audit\"))
                {
                    Directory.CreateDirectory(Environment.CurrentDirectory + @"\audit\");
                }

                XeroIntegration xeroIntegration = new XeroIntegration();

                TextWriter customerAuditTextWriter = new StreamWriter(auditCustomerFile);
                TextWriter invoiceAuditTextWriter = new StreamWriter(auditInvoiceFile);
                TextWriter paymentAuditTextWriter = new StreamWriter(auditPaymentFile);

                var customerCsv = new CsvWriter(customerAuditTextWriter);
                var invoiceCsv = new CsvWriter(invoiceAuditTextWriter);
                var paymentCsv = new CsvWriter(paymentAuditTextWriter);

                bool customerHeaderWritten = false;
                bool invoiceHeaderWritten = false;
                bool paymentHeaderWritten = false;



                SIMSDataEntities dataEntities = new SIMSDataEntities();
                SIMSMapper simsMapper = new SIMSMapper();
                DateTime selectDate = DateTime.Parse("1/1/2017"); //RWF - Debug to make sure we have all the data.
                //DateTime selectDate = DateTime.Now.AddDays(-2);
                var dailyOrderNumbers = dataEntities.order_status_history.Where(p => p.order_status.Equals("com"))
                    .Where(o => o.status_date >= selectDate);
                foreach (var stat in dailyOrderNumbers)
                {
                    try
                    {
                        var orderIdSearch = int.Parse(stat.order_id);
                        Console.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
                        //file.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
                        IEnumerable<order> orderHeaders = dataEntities.orders.Where(o => o.order_id == orderIdSearch);
                        foreach (var header in orderHeaders)
                        {
                            //Check on the Customer / Xero Contact
                            if (header.customer_id != null)
                            {
                                int customerId = int.Parse(header.customer_id.ToString());

                                var xeroContact = simsMapper.BuildContact(customerId);
                                if (!customerHeaderWritten)
                                {
                                    customerCsv.WriteHeader(xeroContact.GetType());
                                    customerHeaderWritten = true;
                                }
                                customerCsv.WriteRecord(xeroContact);
                                xeroContact = xeroIntegration.CreateContact(xeroContact, options.TransmitToXero);

                                string orderid = header.order_id.ToString();
                                order_status_history statusHistory =
                                    dataEntities.order_status_history.Where(o => o.order_id == orderid).FirstOrDefault(d=>d.order_status.Equals("com"));
                                user assignedTo =
                                    dataEntities.users.FirstOrDefault(o => o.user_id == header.assigned_user_id);

                                DateTime completeDate = DateTime.Parse(statusHistory.status_date.ToString());
                                string referenceNumber = assignedTo.first_name.Substring(0, 1).ToUpper() +
                                                         assignedTo.last_name.Substring(0, 1).ToUpper() + " " + header.order_number;
                                //Build Invoice
                                var xeroInvoice = simsMapper.BuildInvoice(header, completeDate, referenceNumber, xeroContact);
                                if (!invoiceHeaderWritten)
                                {
                                    invoiceCsv.WriteHeader(xeroInvoice.GetType());
                                    invoiceHeaderWritten = true;
                                }
                                invoiceCsv.WriteRecord(xeroInvoice);

                                // Create the Invoice
                                xeroInvoice = xeroIntegration.CreateInvoice(xeroInvoice, options.TransmitToXero);


                                //Process Payments
                                var orderPayments = dataEntities.order_payments.Where(o => o.order_id == orderIdSearch).Where(p=>p.payment_type_code != "oth");
                                if (orderPayments.Any())
                                {
                                    Console.WriteLine("  **** Order Payments ****");
                                    //file.WriteLine("  **** Order Payments ****");
                                    foreach (var payment in orderPayments)
                                    {

                                        Payment xeroPayment = simsMapper.BuildPayment(payment, xeroInvoice);

                                        if (!paymentHeaderWritten)
                                        {
                                            paymentCsv.WriteHeader(xeroPayment.GetType());
                                            paymentHeaderWritten = true;
                                        }
                                        paymentCsv.WriteRecord(xeroPayment);
                                        xeroPayment = xeroIntegration.CreatePayment(xeroPayment, options.TransmitToXero);
                                        //Console.WriteLine("  Payment Date:{0} - Payment Amt:{1} - Payment Type:{2}",
                                        //    payment.payment_date, payment.payment_amount, payment.payment_type_code);
                                    }

                                }
                            }
                        }
                    }
                    catch (ValidationException valEx)
                    {
                        _log.ErrorFormat("An Error occurred when processing Orders: {0}", valEx.Message);
                        _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(valEx));
                    }
                    catch (Exception ex)
                    {
                        _log.ErrorFormat("An Error occurred when processing Orders: {0}", ex.Message);
                        _log.ErrorFormat("Stack Trace:{0}", Utilities.FlattenException(ex));
                    }
                }
            customerAuditTextWriter.Close();
            invoiceAuditTextWriter.Close();
            paymentAuditTextWriter.Close();
            }
            
            Console.ReadKey();
        }
    }
}
