using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SIMSData;

namespace XeroInvoiceIntegration
{
    public static class Utilities
    {

        public static string FlattenException(Exception ex)
        {
            StringBuilder sb = new StringBuilder();

            if (ex.InnerException != null)
            {
                sb.Append(FlattenException(ex.InnerException));
            }

            return sb.ToString();
        }
    }
}
