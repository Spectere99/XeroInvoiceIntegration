using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using CsvHelper;
using SIMSData;
using Xero.Api.Core.Model;
using ValidationException = Xero.Api.Infrastructure.Exceptions.ValidationException;
using log4net;

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

                if (!Directory.Exists(Environment.CurrentDirectory + @"\audit\"))
                {
                    Directory.CreateDirectory(Environment.CurrentDirectory + @"\audit\");
                }

                XeroIntegration xeroIntegration = new XeroIntegration(options.TransmitToXero);

                TextWriter customerAuditTextWriter = new StreamWriter(auditCustomerFile);
                TextWriter invoiceAuditTextWriter = new StreamWriter(auditInvoiceFile);
                TextWriter paymentAuditTextWriter = new StreamWriter(auditPaymentFile);

                var customerCsv = new CsvWriter(customerAuditTextWriter);
                var invoiceCsv = new CsvWriter(invoiceAuditTextWriter);
                var paymentCsv = new CsvWriter(paymentAuditTextWriter);

                bool customerHeaderWritten = false;
                bool invoiceHeaderWritten = false;
                bool paymentHeaderWritten = false;

                bool dailyRun = (ConfigurationManager.AppSettings["DailyRun"] == "Y") ? true : false;
                var startDateConfig = ConfigurationManager.AppSettings["StartDate"];

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
                                CustomerAudit customerAudit = new CustomerAudit();
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
                                customerAudit.Address = xeroContact.Addresses.FirstOrDefault().AddressLine1;
                                customerAudit.City = xeroContact.Addresses.FirstOrDefault().City;
                                customerAudit.State = xeroContact.Addresses.FirstOrDefault().Region;
                                customerAudit.Zip = xeroContact.Addresses.FirstOrDefault().PostalCode;
                                customerAudit.ContactEmail = xeroContact.EmailAddress;
                                customerAudit.ContactName = string.Format("{0} {1}",
                                    xeroContact.ContactPersons.FirstOrDefault().FirstName,
                                    xeroContact.ContactPersons.FirstOrDefault().LastName);
                                customerAudit.ContactPhone = string.Format("({0}){1}",
                                    xeroContact.Phones.FirstOrDefault().PhoneAreaCode,
                                    xeroContact.Phones.FirstOrDefault().PhoneNumber);
                                customerAudit.Email = xeroContact.EmailAddress;

                                customerCsv.WriteRecord(customerAudit);

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
                                InvoiceAudit invoiceAudit = new InvoiceAudit();
                                if (!invoiceHeaderWritten)
                                {
                                    invoiceCsv.WriteHeader(invoiceAudit.GetType());
                                    invoiceHeaderWritten = true;
                                }
                                

                                // Create the Invoice
                                if (options.TransmitToXero)
                                {
                                    WaitCheck(1);
                                    xeroInvoice = xeroIntegration.CreateInvoice(xeroInvoice, options.TransmitToXero);
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
                                //Process Payments
                                var orderPayments = dataEntities.order_payments.Where(o => o.order_id == orderIdSearch).Where(p=>p.payment_type_code != "oth");
                                if (orderPayments.Any())
                                {
                                    Console.WriteLine("  **** Order Payments ****");
                                    //file.WriteLine("  **** Order Payments ****");
                                    foreach (var payment in orderPayments)
                                    {

                                        Payment xeroPayment = simsMapper.BuildPayment(payment, xeroInvoice);
                                        PaymentAudit paymentAudit = new PaymentAudit();
                                        if (!paymentHeaderWritten)
                                        {
                                            paymentCsv.WriteHeader(paymentAudit.GetType());
                                            paymentHeaderWritten = true;
                                        }
                                        
                                        if (options.TransmitToXero)
                                        {
                                            WaitCheck(1);
                                            xeroPayment = xeroIntegration.CreatePayment(xeroPayment, options.TransmitToXero);

                                            var pymt = from pay in dataEntities.order_payments
                                                where pay.order_payment_id == payment.order_payment_id
                                                select pay;

                                            order_payments updPayment = pymt.Single();

                                            updPayment.xero_payment_id = xeroPayment.Id.ToString();

                                            dataEntities.SaveChanges();
                                        }

                                        paymentAudit.CheckNumber = payment.check_number;
                                        paymentAudit.OrderId = header.order_id;
                                        paymentAudit.OrderNumber = header.order_number;
                                        paymentAudit.PaymentAmount = payment.payment_amount;
                                        paymentAudit.PaymentDate = payment.payment_date;
                                        paymentAudit.PaymentID = payment.order_payment_id;
                                        paymentAudit.PaymentType = payment.payment_type_code;
                                        paymentAudit.XeroPaymentId = xeroPayment.Id.ToString();

                                        paymentCsv.WriteRecord(paymentAudit);
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
            //SendCompleteEmail(auditCustomerFile, auditInvoiceFile, auditPaymentFile);
            }
            
            Console.ReadKey();
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

        private static void SendCompleteEmail(string customerAttch, string invoiceAttch, string paymentAttch)
        {
            //var callbackUrl = Url.Action("ConvertReservation", "Registration", new { userId = user.Person.PersonId, code = user.RegistrationCode }, protocol: Request.Url.Scheme);

            System.Net.Mail.MailMessage m = new System.Net.Mail.MailMessage(
            new System.Net.Mail.MailAddress("rflowers@saber98.com", "Xero Integration"),
            new System.Net.Mail.MailAddress("daphnepaw@gmail.com"));
            m.To.Add("flowersr99@gmail.com");
            m.Subject = "Xero Integration Complete";
            m.Body = string.Format("Nightly Xero Integration has completed.  Attached are the nightly files.");

            m.Attachments.Add(new System.Net.Mail.Attachment(customerAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(invoiceAttch));
            m.Attachments.Add(new System.Net.Mail.Attachment(paymentAttch));

            m.IsBodyHtml = true;
            System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient("mail.saber98.com");
            smtp.UseDefaultCredentials = false;
            smtp.Credentials = new System.Net.NetworkCredential("rflowers@saber98.com", "Sp3ct3r399");

            smtp.EnableSsl = false;
            smtp.Send(m);
        }

    }
}
