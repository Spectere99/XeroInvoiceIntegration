using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
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
            //setup logger
            _log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

            //setup audit file
            string auditCustomerFile = Environment.CurrentDirectory + @"\audit\" + DateTime.Now.ToString("yyyyMMddhhmmss") + "_Customer.csv";
            string auditInvoiceFile = Environment.CurrentDirectory + @"\audit\" + DateTime.Now.ToString("yyyyMMddhhmmss") + "_Invoice.csv";
            string auditPaymentFile = Environment.CurrentDirectory + @"\audit\" + DateTime.Now.ToString("yyyyMMddhhmmss") + "_Payment.csv";

            if (!Directory.Exists(Environment.CurrentDirectory + @"\audit\"))
            {
                Directory.CreateDirectory(Environment.CurrentDirectory + @"\audit\");
            }

            XeroIntegration xeroIntegration = new XeroIntegration();

            TextWriter customerAuditTextWriter = new StreamWriter(auditCustomerFile);
            TextWriter invoiceAuditTextWriter = new StreamWriter(auditInvoiceFile);
            TextWriter auditPaymentTextWriter = new StreamWriter(auditPaymentFile);

            var customerCsv = new CsvWriter(customerAuditTextWriter);
            var invoiceCsv = new CsvWriter(invoiceAuditTextWriter);
            var paymentCsv = new CsvWriter(auditPaymentTextWriter);



            SIMSDataEntities dataEntities = new SIMSDataEntities();
            SIMSMapper simsMapper = new SIMSMapper();
                DateTime selectDate = DateTime.Parse("1/20/2017"); //RWF - Debug to make sure we have all the data.
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
                                customerCsv.WriteRecord(xeroContact);
                                xeroContact = xeroIntegration.CreateContact(xeroContact);

                                var xeroInvoice = simsMapper.BuildInvoice(header, xeroContact);
                                //Build Invoice
                                
                                

                                Console.WriteLine(
                                    "  Order Header- OrderDate:{0} - OrderNumber:{1} - Shipping:{2} - OrderTotal:{3}",
                                    header.order_date, header.order_number, header.shipping, header.total);
                                //file.WriteLine("  Order Header- OrderDate:{0} - OrderNumber:{1} - Shipping:{2} - OrderTotal:{3}", header.order_date, header.order_number, header.shipping, header.total);
                                IEnumerable<order_detail> orderDetails =
                                    dataEntities.order_detail.Where(o => o.order_id == header.order_id);
                                Console.WriteLine("  **** Order Line Items ****");
                                //file.WriteLine("  **** Order Line Items ****");
                                xeroInvoice.LineItems = new List<LineItem>();
                                foreach (var detail in orderDetails)
                                {
                                    int priceListId = detail.pricelist_id ?? default(int);
                                    var pricelistItems =
                                        dataEntities.pricelists.FirstOrDefault(p => p.pricelist_id == priceListId);

                                    LineItem xeroInvoiceItem = new LineItem();
                                    xeroInvoiceItem.AccountCode = "400";
                                    xeroInvoiceItem.Description = pricelistItems.pricelist_description;
                                    //xeroInvoiceItem.ItemCode = pricelistItems.pricelist_code;
                                    xeroInvoiceItem.Quantity = detail.item_quantity;
                                    xeroInvoiceItem.LineAmount = decimal.Parse(detail.item_price_ext);
                                    xeroInvoiceItem.UnitAmount = decimal.Parse(detail.item_price_each);


                                    xeroInvoice.LineItems.Add(xeroInvoiceItem);
                                    Console.WriteLine(
                                        "    Line Item:{0} - Desc.:{1} - Unit Price:{2} - Qty:{3} - Ext Price:{4}",
                                        detail.item_line_number, pricelistItems.pricelist_description,
                                        detail.item_price_each, detail.item_quantity, detail.item_price_ext);
                                    //file.WriteLine("    Line Item:{0} - Desc.:{1} - Unit Price:{2} - Qty:{3} - Ext Price:{4}", detail.item_line_number, pricelistItems.pricelist_description, detail.item_price_each, detail.item_quantity, detail.item_price_ext);
                                }

                                invoiceCsv.WriteRecord(xeroInvoice);
                                // Create the Invoice
                                xeroInvoice = xeroIntegration.CreateInvoice(xeroInvoice);

                                //Process Payments
                                var orderPayments = dataEntities.order_payments.Where(o => o.order_id == orderId);
                                if (orderPayments.Any())
                                {
                                    Console.WriteLine("  **** Order Payments ****");
                                    //file.WriteLine("  **** Order Payments ****");
                                    foreach (var payment in orderPayments)
                                    {
                                        Payment xeroPayment = new Payment();
                                        xeroPayment.Invoice = xeroInvoice;
                                        xeroPayment.Date = DateTime.Parse(payment.payment_date.ToString());
                                        xeroPayment.Amount = Decimal.Parse(payment.payment_amount);
                                        xeroPayment.Type = PaymentType.AccountsReceivable;
                                        xeroPayment.Reference = string.Format("{0}{1}", payment.order_id,
                                            payment.payment_date);

                                        paymentCsv.WriteRecord(xeroPayment);
                                        xeroPayment = xeroIntegration.CreatePayment(xeroPayment);
                                        Console.WriteLine("  Payment Date:{0} - Payment Amt:{1} - Payment Type:{2}",
                                            payment.payment_date, payment.payment_amount, payment.payment_type_code);

                                        //file.WriteLine("  Payment Date:{0} - Payment Amt:{1} - Payment Type:{2}",
                                        //    payment.payment_date, payment.payment_amount, payment.payment_type_code);
                                    }

                                }
                                //file.WriteLine("##*** END of ORDER ****");
                            }
                        }

                    }
                    }
                    catch (ValidationException ex)
                    {

                        file.WriteLine(string.Format("!!!## An Error occurred with Order Number {0}:  Details: {1}", stat.order_id, ex.Message));
                    }
                    
                }
            }
            Console.ReadKey();
 
    }
}
