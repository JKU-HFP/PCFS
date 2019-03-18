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
        private long[] _timeBins;
        private byte _chan0;
        private byte _chan1;

        //Properties
        public double StagePosition { get; set; }
        public double NumScans { get; set; }
        public double PerformedScans { get; private set; }
        public long OffsetCh1 { get; set; } = 0;

        public long TotalTime { get; private set; } = 0;
        public long TotalCountsCh0 { get; private set; } = 0;
        public long TotalCountsCh1 { get; private set; } = 0;

        public double NormalizationFactor { get; private set; } = 1.0;

        public long[] HistogramX { get; private set; }
        public long[] HistogramY { get; private set; }
        public double[] HistogramYErr { get; private set; }
        public double[] HistogramYNorm { get; private set; }
        public double[] HistogramYNormErr { get; private set; }


        public PCFSPoint(BinningListHistogram binningListHistogram, ulong timeWindow)
        {
            _timeBins = binningListHistogram.Binnings.Select(p => Math.Abs( p.high - p.low )).ToArray();
            _chan0 = binningListHistogram.CorrelationConfig[0].cA;
            _chan1 = binningListHistogram.CorrelationConfig[0].cB;

            _correlator = new Kurolator(new List<CorrelationGroup> { binningListHistogram }, timeWindow);

            HistogramX = _correlator[0].Histogram_X;

            PerformedScans = 0;
        }

        public void AddMeasurement(List<TimeTags> timetags, long integrationTime)
        {
            TotalTime += integrationTime;
         
            foreach(TimeTags tt in timetags)
            {
                TotalCountsCh0 += tt.TotalCounts[_chan0];
                TotalCountsCh1 += tt.TotalCounts[_chan1];

                _correlator.AddCorrelations(tt, tt, OffsetCh1);
                _correlator[0].ClearAllCorrelations(); //Clear correlations to save memory
            }

            //Histograms and Normalization
            NormalizationFactor = TotalTime / ((double)TotalCountsCh0 * (double)TotalCountsCh1);

            HistogramY = _correlator[0].Histogram_Y;
            HistogramYErr = HistogramY.Select(p => Math.Sqrt(p)).ToArray();
            
            HistogramYNorm = HistogramY.Zip(_timeBins, (yval, bin) => yval * (NormalizationFactor / bin) ).ToArray();
            HistogramYNormErr = HistogramYErr.Zip(_timeBins, (yval, bin) => yval * (NormalizationFactor / bin)).ToArray();

            PerformedScans++;
        }

    }
}
