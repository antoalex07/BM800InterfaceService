using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestService.Models
{
    /// <summary>
    /// Represents the parsed data from the GLUQUANT HBA1C analyzer message within <R> tags.
    /// </summary>
    public class AnalyzerResult
    {
        public string HbA1ab { get; set; } = string.Empty;
        public string HbF { get; set; } = string.Empty;
        public string HbLa1c { get; set; } = string.Empty;
        public string HbA1c { get; set; } = string.Empty;
        // Note: HbA1 is mentioned in the example but not in the description list. Including it for completeness based on example.
        public string HbA1 { get; set; } = string.Empty;
        public string HbA0 { get; set; } = string.Empty;

        // You can add properties for <M> and <I> sections if needed later
        // public string MachineModel { get; set; } = string.Empty;
        // public string SerialNumber { get; set; } = string.Empty;
        // public string SampleType { get; set; } = string.Empty;
        // etc.
    }
}
