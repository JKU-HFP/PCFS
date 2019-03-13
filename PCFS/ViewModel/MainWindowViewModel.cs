using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PIStage_Library;
using System.Diagnostics;
using TimeTagger_Library;

namespace PCFS.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
    {
        HydraHarpTagger tagger;

        public MainWindowViewModel()
        {
            PI_GCS2_Stage linearstage = new PI_GCS2_Stage(null);
            linearstage.Connect("C-863");

            Stopwatch sw = new Stopwatch();

            while (true)
            {
                linearstage.SetVelocity(25);
                linearstage.Move_Absolute(0);
                linearstage.SetVelocity(25);
                linearstage.Move_Absolute(20);
                linearstage.WaitForPos();
                sw.Start();
                linearstage.SetVelocity(0.005);
                linearstage.Move_Relative(0.05);
                linearstage.WaitForPos();
                sw.Stop();
                linearstage.SetVelocity(25);
                linearstage.Move_Absolute(200);
                linearstage.WaitForPos();
            }

            tagger = new HydraHarpTagger(null);
            tagger.PacketSize = 500;

            tagger.TimeTagsCollected += Tagger_TimeTagsCollected;

            tagger.StartCollectingTimeTagsAsync();

        }

        private void Tagger_TimeTagsCollected(object sender, TimeTagsCollectedEventArgs e)
        {
            tagger.StopCollectingTimeTags();
        }
    }
}
