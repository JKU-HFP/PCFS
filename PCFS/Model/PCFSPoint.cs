using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TimeTagger_Library;

namespace PCFS.Model
{
    public class PCFSPoint
    {
        //Private fields
        private Kurolator _correlator;

        //Properties
        public double StagePosition { get; set; }
        public double NumScans { get; set; }
        public double PerformedScans { get; private set; }

        public PCFSPoint(BinningListHistogram binningListHistogram, ulong timeWindow)
        {
            _correlator = new Kurolator(new List<CorrelationGroup> { binningListHistogram }, timeWindow);
            PerformedScans = 0;
        }
    }
}
