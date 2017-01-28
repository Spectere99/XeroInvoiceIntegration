using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIMSData;

namespace XeroInvoiceIntegration
{
    class Program
    {
        static void Main(string[] args)
        {

            string auditFile = Environment.CurrentDirectory + @"\logs\" + DateTime.Now.ToString("yyyyMMddhhmmss") + ".txt";
            if (!Directory.Exists(Environment.CurrentDirectory + @"\logs\"))
            {
                Directory.CreateDirectory(Environment.CurrentDirectory + @"\logs\");
            }
            XeroIntegration xeroIntegration = new XeroIntegration();

            xeroIntegration.CreateContact();

            //using (StreamWriter file = new StreamWriter(auditFile))
            //{ 
            //    SIMSDataEntities dataEntities = new SIMSDataEntities();
            //    DateTime selectDate = DateTime.Parse("1/20/2017");  //RWF - Debug to make sure we have all the data.
            //    //DateTime selectDate = DateTime.Now.AddDays(-2);
            //    var dailyOrderNumbers = dataEntities.order_status_history.Where(p => p.order_status.Equals("com"))
            //        .Where(o => o.status_date >= selectDate);
            //    foreach (var stat in dailyOrderNumbers)
            //    {
            //        var orderId = int.Parse(stat.order_id);
            //        Console.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
            //        file.WriteLine("##ORDER #: {0} - Date: {1}", stat.order_id, stat.status_date);
            //        IEnumerable<order> orderHeaders = dataEntities.orders.Where(o => o.order_id == orderId);
            //        foreach (var header in orderHeaders)
            //        {
            //            Console.WriteLine("  Order Header- OrderDate:{0} - OrderNumber:{1} - Shipping:{2} - OrderTotal:{3}", header.order_date, header.order_number, header.shipping, header.total);
            //            file.WriteLine("  Order Header- OrderDate:{0} - OrderNumber:{1} - Shipping:{2} - OrderTotal:{3}", header.order_date, header.order_number, header.shipping, header.total);
            //            IEnumerable<order_detail> orderDetails = dataEntities.order_detail.Where(o => o.order_id == header.order_id);
            //            Console.WriteLine("  **** Order Line Items ****");
            //            file.WriteLine("  **** Order Line Items ****");
            //            foreach (var detail in orderDetails)
            //            {
            //                int priceListId = detail.pricelist_id ?? default(int);
            //                var pricelistItems = dataEntities.pricelists.FirstOrDefault(p => p.pricelist_id == priceListId);

            //                Console.WriteLine("    Line Item:{0} - Desc.:{1} - Unit Price:{2} - Qty:{3} - Ext Price:{4}", detail.item_line_number, pricelistItems.pricelist_description, detail.item_price_each, detail.item_quantity, detail.item_price_ext);
            //                file.WriteLine("    Line Item:{0} - Desc.:{1} - Unit Price:{2} - Qty:{3} - Ext Price:{4}", detail.item_line_number, pricelistItems.pricelist_description, detail.item_price_each, detail.item_quantity, detail.item_price_ext);
            //            }

            //            var orderPayments = dataEntities.order_payments.Where(o => o.order_id == orderId);
            //            if (orderPayments.Any())
            //            {
            //                Console.WriteLine("  **** Order Payments ****");
            //                file.WriteLine("  **** Order Payments ****");
            //                foreach (var payment in orderPayments)
            //                {
            //                    Console.WriteLine("  Payment Date:{0} - Payment Amt:{1} - Payment Type:{2}",
            //                        payment.payment_date, payment.payment_amount, payment.payment_type_code);

            //                    file.WriteLine("  Payment Date:{0} - Payment Amt:{1} - Payment Type:{2}",
            //                        payment.payment_date, payment.payment_amount, payment.payment_type_code);
            //                }

            //            }
            //            file.WriteLine("##*** END of ORDER ****");
            //        }
            //    }
            //}
            Console.ReadKey();
        }
 
    }
}
