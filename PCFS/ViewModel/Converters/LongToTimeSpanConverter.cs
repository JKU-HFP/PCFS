using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace PCFS.ViewModel.Converters
{
    [ValueConversion(typeof(long), typeof(string))]
    public class LongToTimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long time = (long)value;
            long factor = long.Parse((string)parameter); //To Milliseconds

            TimeSpan ts = new TimeSpan(0, 0, 0, 0, (int)(time / factor));
            return ts.ToString("hh\\:mm\\:ss\\:ffff");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            TimeSpan ts = (TimeSpan)value;
            return ts.Milliseconds;
        }
    }
}
