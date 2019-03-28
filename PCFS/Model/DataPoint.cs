using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TimeTagger_Library;

namespace PCFS.Model
{
    public class DataPoint : INotifyPropertyChanged
    {
        //Private fields
        private Kurolator _PCFSCorrelator;
        private Kurolator _G2PreviewCorrelator;

        private long[] _timeBins;
        private byte _chan0;
        private byte _chan1;

        //Tracked Properties
        private int _index = 0;
        public int Index
        {
            get { return _index; }
            set
            {
                _index = value;
                OnPropertyChanged("Index");
            }
        }

        private double _stagePosition = 0.0;
        public double StagePosition
        {
            get { return _stagePosition; }
            set
            {
                _stagePosition = value;
                OnPropertyChanged("StagePosition");
            }
        }

        private int _performedScans = 0;
        public int PerformedScans
        {
            get { return _performedScans; }
            set
            {
                _performedScans = value;
                OnPropertyChanged("PerformedScans");
            }
        }

        private long _totalTime = 0;
        public long TotalTime
        {
            get { return _totalTime; }
            set
            {
                _totalTime = value;
                OnPropertyChanged("TotalTime");
            }
        }

        private double _averageCoincPerSecond;

        public double AverageCoincPerSecond
        {
            get { return _averageCoincPerSecond; }
            set
            {
                _averageCoincPerSecond = value;
                OnPropertyChanged("AverageCoincPerSecond");
            }
        }


        //Properties
        public double NumScans { get; set; }   
        public long OffsetCh1 { get; set; } = 0;
               
        public long TotalCountsCh0 { get; private set; } = 0;
        public long TotalCountsCh1 { get; private set; } = 0;
        public long TotalCounts
        {
            get => TotalCountsCh0 + TotalCountsCh1;
        }

        public double NormalizationFactor { get; private set; } = 1.0;

        public long[] HistogramX { get; private set; }
        public long[] HistogramY { get; private set; }
        public double[] HistogramYErr { get; private set; }
        public double[] HistogramYNorm { get; private set; }
        public double[] HistogramYNormErr { get; private set; }

        public long[] HistogramXPreview { get; private set; }
        public long[] HistogramYPreview { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void OnPropertyChanged(string propname)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propname));
        }

        public DataPoint(BinningListHistogram binningListHistogram, ulong timeWindow)
        {
            _timeBins = binningListHistogram.Binnings.Select(p => Math.Abs( p.high - p.low )).ToArray();
            _chan0 = binningListHistogram.CorrelationConfig[0].cA;
            _chan1 = binningListHistogram.CorrelationConfig[0].cB;

            //Setup PCFS Correlator
            _PCFSCorrelator = new Kurolator(new List<CorrelationGroup> { binningListHistogram }, timeWindow);

            //Setup G2 Preview Correlator
            ulong previewTimeWindow = 100000;

            Histogram g2previewGroup = new Histogram(binningListHistogram.CorrelationConfig, previewTimeWindow, 256);
            _G2PreviewCorrelator = new Kurolator(new List<CorrelationGroup> { g2previewGroup }, previewTimeWindow);

            HistogramX = _PCFSCorrelator[0].Histogram_X;
            HistogramXPreview = _G2PreviewCorrelator[0].Histogram_X;

            PerformedScans = 0;
        }

        public void AddMeasurement(List<TimeTags> timetags, long integrationTime)
        {
            TotalTime += integrationTime;
         
            foreach(TimeTags tt in timetags)
            {
                TotalCountsCh0 += tt.TotalCounts[_chan0];
                TotalCountsCh1 += tt.TotalCounts[_chan1];

                _PCFSCorrelator.AddCorrelations(tt, tt, OffsetCh1);
                _PCFSCorrelator[0].ClearAllCorrelations(); //Clear correlations to save memory

                _G2PreviewCorrelator.AddCorrelations(tt, tt, OffsetCh1);
                //_G2PreviewCorrelator[0].ClearAllCorrelations(); //Clear correlations to save memory
            }

            //Histograms and Normalization
            NormalizationFactor = TotalTime / ((double)TotalCountsCh0 * (double)TotalCountsCh1);

            HistogramY = _PCFSCorrelator[0].Histogram_Y;
            HistogramYErr = HistogramY.Select(p => Math.Sqrt(p)).ToArray();

            AverageCoincPerSecond = HistogramY.Zip(_timeBins, (histY, bin) => histY / (bin / 1000000000000.0)).Average();

            HistogramYNorm = HistogramY.Zip(_timeBins, (yval, bin) => yval * (NormalizationFactor / bin) ).ToArray();
            HistogramYNormErr = HistogramYErr.Zip(_timeBins, (yval, bin) => yval * (NormalizationFactor / bin)).ToArray();

            HistogramYPreview = _G2PreviewCorrelator[0].Histogram_Y;

            PerformedScans++;
        }

    }
}
