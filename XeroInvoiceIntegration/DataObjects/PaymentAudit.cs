using System;

namespace XeroInvoiceIntegration.DataObjects
{
    public class PaymentAudit
    {
        public int PaymentID { get; set; }
        public string XeroPaymentId { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string PaymentType { get; set; }
        public string CheckNumber { get; set; }
        public string PaymentAmount { get; set; }
        public string OrderNumber { get; set; }
        public int OrderId { get; set; }

    }
}
