using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCFS.Model
{
    public class PCFSCurve
    {
        public (long low, long high) Binning { get; set; }

        public string BinningString
        {
            get
            {
                return FormattedTime(Binning.low) + " - " + FormattedTime(Binning.high);
            }
        }
        
        public double[] positions { get; set; }
        public double[] G2 { get; set; }
        public double[] G2Err { get; set; }
        public double[] G2Norm { get; set; }
        public double[] G2NormErr { get; set; }
        public double RenormFactor = 1.0;

        public double[] Energy { get; set; }
        public double[] pE { get; set; }
        public double[] PEErr { get; set; }

        public PCFSCurve()
        {

        }
        
        public static string FormattedTime(long time)
        {
            if (time >= 1000000000) return (time / 1000000000).ToString()+" ms";
            if (time >= 1000000) return (time / 1000000).ToString() + " μs";
            if (time >= 1000) return (time / 1000).ToString() + " ns";

            return time.ToString() + "ps";
        }


    }
}
