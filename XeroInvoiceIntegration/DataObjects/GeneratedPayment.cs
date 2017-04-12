using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xero.Api.Core.Model;

namespace XeroInvoiceIntegration.DataObjects
{
    public class GeneratedPayment
    {
        public PaymentAudit PaymentAudit { get; set; }
        public Payment Payment { get; set; }

    }
}
