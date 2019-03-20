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
                return Binning.low.ToString() + " - " + Binning.high.ToString();
            }
        }
        
        public double[] positions { get; set; }
        public double[] G2 { get; set; }
        public double[] G2Err { get; set; }

        public double[] Energy { get; set; }
        public double[] pE { get; set; }
        public double[] PEErr { get; set; }

        public PCFSCurve()
        {

        }
    }
}
