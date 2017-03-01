using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XeroInvoiceIntegration
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
