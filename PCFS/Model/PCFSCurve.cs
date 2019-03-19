using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCFS.Model
{
    public class PCFSCurve
    {       
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
