using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using SIMSData;

namespace XeroInvoiceIntegration
{
    public static class Utilities
    {

        public static string FlattenException(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ex.Message);
            if (ex.InnerException != null)
            {
                sb.AppendLine(FlattenException(ex.InnerException));
            }

            return sb.ToString();
        }

        public static string FormatXML(string xml)
        {
            try
            {
                XDocument doc = XDocument.Parse(xml);
                return doc.ToString();
            }
            catch (Exception)
            {
                return xml;
            }
        }
    }
}
