using System;
using System.Collections.Generic;
using System.Data.Entity.Core.Objects;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace XeroInvoiceIntegration
{
    class Options
    {

        [Option('x', null,  HelpText = "Transmit Orders to Xero.")]
        public bool TransmitToXero { get; set; }

        [Option('o', "auditpath", Required = false, HelpText = "Location for Audit files (will default if not set).")]
        public string AuditFileLocation { get; set; }
        

        [ParserState]
        public IParserState LastParserState { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
