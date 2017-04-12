using System;
using System.Linq;
using SIMSData;
using Xero.Api.Core.Model;

namespace XeroInvoiceIntegration
{
    public static class Common
    {
        //public static void CloseOrder(int orderId)
        //{
        //    SIMSDataEntities db = new SIMSDataEntities();
        //    var orderStatusHistory = db.order_status_history.OrderByDescending(p => p.order_status_history_id)
        //        .FirstOrDefault();
        //    if (orderStatusHistory != null)
        //    {
        //        int clsdPk = orderStatusHistory
        //                         .order_status_history_id +
        //                     1;
        //        order_status_history orderClosedStatus = new order_status_history();
        //        orderClosedStatus.order_status_history_id = clsdPk;
        //        orderClosedStatus.order_id = orderId.ToString();
        //        orderClosedStatus.order_status = "clos";
        //        orderClosedStatus.status_date = DateTime.Now;

        //        db.order_status_history.Add(orderClosedStatus);
        //    }
        //    db.SaveChanges();
        //}

        public static void RecordXeroPaymentControl(string orderNumber, order_payments simsPayment, string xeroPaymentId)
        {
            SIMSDataEntities db = new SIMSDataEntities();
            //Create new record for Invoice_Control table to say we have created and sent this invoice
            var paymentItems =
                db.payment_interface_control.OrderByDescending(p => p.payment_control_id).FirstOrDefault();
            if (paymentItems != null)
            {
                int clsdPk = paymentItems.payment_control_id + 1;

                payment_interface_control paymentControl = new payment_interface_control();

                paymentControl.payment_control_id = clsdPk;
                paymentControl.order_id = int.Parse(simsPayment.order_id.ToString());
                paymentControl.payment_date = DateTime.Now;
                paymentControl.order_number = orderNumber;
                paymentControl.order_payment_id = simsPayment.order_payment_id;
                paymentControl.xero_payment_id = xeroPaymentId;

                db.payment_interface_control.Add(paymentControl);
                db.SaveChanges();    
            }
            
        }
        public static void RecordXeroPaymentControl(string orderNumber, int orderPaymentId, int orderId, string xeroPaymentId)
        {
            SIMSDataEntities db = new SIMSDataEntities();
            //Create new record for Invoice_Control table to say we have created and sent this invoice
            var paymentItems =
                db.payment_interface_control.OrderByDescending(p => p.payment_control_id).FirstOrDefault();
            if (paymentItems != null)
            {
                int clsdPk = paymentItems.payment_control_id + 1;

                payment_interface_control paymentControl = new payment_interface_control();

                paymentControl.payment_control_id = clsdPk;
                paymentControl.order_id = orderId;
                paymentControl.payment_date = DateTime.Now;
                paymentControl.order_number = orderNumber;
                paymentControl.order_payment_id = orderPaymentId;
                paymentControl.xero_payment_id = xeroPaymentId;

                db.payment_interface_control.Add(paymentControl);
                db.SaveChanges();
            }

        }

        public static void RecordXeroInvoiceControl(string orderNumber, order simsOrder, Invoice xeroInvoice)
        {
            
        }
        //public static void UpdateSIMSPaymentComplete(Invoice xeroInvoice, order_payments simsPayment)
        //{
        //    SIMSDataEntities db = new SIMSDataEntities();
        //    var payment = xeroInvoice.Payments.SingleOrDefault(p => p.Amount == decimal.Parse(simsPayment.payment_amount));
        //    if (payment != null)
        //    {
        //        var pymt = from pay in db.order_payments
        //                   where pay.order_payment_id == simsPayment.order_payment_id
        //                   select pay;

        //        order_payments updPayment = pymt.Single();

        //        updPayment.xero_payment_id = payment.Id.ToString();

        //        db.SaveChanges();
        //    }
        //}

        //public static void UpdateSIMSPaymentComplete(Guid paymentId, order_payments payment)
        //{
        //    SIMSDataEntities db = new SIMSDataEntities();
        //    var pymt = from pay in db.order_payments
        //               where
        //               pay.order_payment_id == payment.order_payment_id
        //               select pay;
        //    order_payments updPayment = pymt.Single();

        //    updPayment.xero_payment_id = paymentId.ToString();

        //    db.SaveChanges();
        //}
    }
}
