using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XeroInvoiceIntegration
{
    public class ExceptionAudit
    {
        public string OrderId { get; set; }
        public string OrderNumber { get; set; }
        public string XeroInvoiceId { get; set; }
        public int CustomerID { get; set; }
        public string XeroCustomerID { get; set; }
        public int PaymentID { get; set; }
        public string XeroPaymentId { get; set; }
        public string CustomerName { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string ContactName { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public string ReferenceNbr { get; set; }
        public DateTime? InvoiceCreateDate { get; set; }
        public DateTime? InvoiceDueDate { get; set; }
        public int LineItemCount { get; set; }
        public decimal? InvoiceAmt { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string PaymentType { get; set; }
        public string CheckNumber { get; set; }
        public string PaymentAmount { get; set; }
        public string ExceptionMessage { get; set; }
    }
}
