using System;

namespace XeroInvoiceIntegration.DataObjects
{
    public class InvoiceAudit
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public string XeroInvoiceId { get; set; }
        public string ReferenceNbr { get; set; }
        public DateTime? CreateDate { get; set; }
        public DateTime? InvoiceDueDate { get; set; }
        public int LineItemCount { get; set; }
        public decimal? InvoiceAmt { get; set; }
        
    }
}
