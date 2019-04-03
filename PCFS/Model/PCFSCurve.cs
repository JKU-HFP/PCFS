using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCFS.Model
{
    public class PCFSCurve : INotifyPropertyChanged
    {
        //Tracked properties
        private double _renormFactor = 1.0;
        public double RenormFactor
        {
            get { return _renormFactor; }
            set
            {
                _renormFactor = value;
                OnPropertyChanged("RenormFactor");
            }
        }

        private double _averageRelErrorG2 = 0;
        public double AverageRelErrorG2
        {
            get { return _averageRelErrorG2; }
            set
            {
                _averageRelErrorG2 = value;
                OnPropertyChanged("AverageRelErrorG2");
            }
        }

        private double _ErrorPE;
        public double ErrorPE
        {
            get { return _ErrorPE; }
            set
            {
                _ErrorPE = value;
                OnPropertyChanged("ErrorPE");
            }
        }



        //Properties
        public (long low, long high) Binning { get; set; }

        public string BinningString
        {
            get
            {
                return FormattedTime(Binning.low,false) + " - " + FormattedTime(Binning.high,false);
            }
        }

        public string BinningStringUnicode
        {
            get
            {
                return FormattedTime(Binning.low, true) + " - " + FormattedTime(Binning.high, true);
            }
        }

        public double[] positions { get; set; }
        public double[] G2 { get; set; }
        public double[] G2Err { get; set; }
        public double[] G2Norm { get; set; }
        public double[] G2NormErr { get; set; }

        public double[] Energy { get; set; }
        public double[] pE { get; set; }
        public double[] PEErr { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void OnPropertyChanged(string propname)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propname));
        }

        public PCFSCurve()
        {

        }
        
        public static string FormattedTime(long time, bool unicode)
        {
            if (Math.Abs(time) >= 1000000000) return (time / 1000000000).ToString()+" ms";
            if (Math.Abs(time) >= 1000000) return (time / 1000000).ToString() + (unicode ? "us" : " μs");
            if (Math.Abs(time) >= 1000) return (time / 1000).ToString() + " ns";

            return time.ToString() + "ps";
        }


    }
}
