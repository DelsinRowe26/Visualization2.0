using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using CSCore;
using CSCore.Codecs;
using CSCore.DSP;
using CSCore.SoundOut;
using CSCore.SoundIn;
using CSCore.Streams;
using CSCore.Streams.Effects;
using CSCore.CoreAudioAPI;
using WinformsVisualization.Visualization;
using System.Activities.Statements;
using System.Threading;

namespace WinformsVisualization
{
    public partial class Form1 : Form
    {
        private WasapiCapture _soundIn;
        private ISoundOut _soundOut;
        private IWaveSource _source;
        private PitchShifter _pitchShifter;
        private LineSpectrum _lineSpectrum;
        private VoicePrint3DSpectrum _voicePrint3DSpectrum;
        private int stop;
        private int click;
        private readonly Bitmap _bitmap = new Bitmap(2000, 600);
        private int _xpos;
        private OpenFileDialog openFileDialog;

        public Form1()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            click = 1;
            openFileDialog = new OpenFileDialog()
            {
                Filter = CodecFactory.SupportedFilesFilterEn,
                Title = "Select a file..."
            };
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                
                Stop();
                do
                {
                    //open the selected file
                    ISampleSource source = CodecFactory.Instance.GetCodec(openFileDialog.FileName)
                        .ToSampleSource()
                        .AppendSource(x => new PitchShifter(x), out _pitchShifter);

                    /*while(click != 0)
                    {
                        if (source.Position == 0.4)
                        {
                            source.Position = 0;
                        }
                    }*/

                    SetupSampleSource(source);

                    //play the audio

                    _soundOut = new WasapiOut();
                    _soundOut.Initialize(_source);
                    _soundOut.Play();
                    //Thread.Sleep(200);
                } while (click != 0);
                timer1.Start();

                    propertyGridTop.SelectedObject = _lineSpectrum;
                    propertyGridBottom.SelectedObject = _voicePrint3DSpectrum;
                
            }
        }

        public ISampleSource BandPassFilter(int sampleRate, int bottomFreq, int topFreq)
        {
            var sampleSource = CodecFactory.Instance.GetCodec(openFileDialog.FileName)
                    .ToSampleSource()
                    .AppendSource(x => new PitchShifter(x), out _pitchShifter);
            var tempFilter = sampleSource.AppendSource(x => new BiQuadFilterSource(x));
            tempFilter.Filter = new HighpassFilter(sampleRate, bottomFreq);
            var filteredSource = tempFilter.AppendSource(x => new BiQuadFilterSource(x));
            filteredSource.Filter = new LowpassFilter(sampleRate, topFreq);

            return filteredSource;
        }

        private void fromDefaultDeviceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Stop();

            //open the default device/открыть устройство по умолчанию
            _soundIn = new WasapiCapture();
            //Our loopback capture opens the default render device by default so the following is not needed
            //Наш циклический захват по умолчанию открывает устройство рендеринга по умолчанию, поэтому следующее не требуется.
            //_soundIn.Device = MMDeviceEnumerator.DefaultAudioEndpoint(DataFlow.Render, Role.Console);
            _soundIn.Initialize();

            var soundInSource = new SoundInSource(_soundIn);
            ISampleSource source = soundInSource.ChangeSampleRate(16000).ToSampleSource().AppendSource(x => new PitchShifter(x), out _pitchShifter);

            SetupSampleSource(source);

            //play the audio
            _soundIn.Start();

            _soundOut = new WasapiOut();
            _soundOut.Initialize(_source);
            _soundOut.Play();

            timer1.Start();

            propertyGridTop.SelectedObject = _lineSpectrum;
            propertyGridBottom.SelectedObject = _voicePrint3DSpectrum;
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="aSampleSource"></param>
        private void SetupSampleSource(ISampleSource aSampleSource)
        {
            const FftSize fftSize = FftSize.Fft4096;
            //create a spectrum provider which provides fft data based on some input
            var spectrumProvider = new BasicSpectrumProvider(aSampleSource.WaveFormat.Channels,
                aSampleSource.WaveFormat.SampleRate, fftSize);

            //linespectrum and voiceprint3dspectrum used for rendering some fft data
            //in oder to get some fft data, set the previously created spectrumprovider 
            _lineSpectrum = new LineSpectrum(fftSize)
            {
                SpectrumProvider = spectrumProvider,
                UseAverage = true,
                BarCount = 50,
                BarSpacing = 2,
                IsXLogScale = true,
                ScalingStrategy = ScalingStrategy.Sqrt
            };
            _voicePrint3DSpectrum = new VoicePrint3DSpectrum(fftSize)
            {
                SpectrumProvider = spectrumProvider,
                UseAverage = true,
                PointCount = 200,
                IsXLogScale = true,
                ScalingStrategy = ScalingStrategy.Sqrt
            };

            //the SingleBlockNotificationStream is used to intercept the played samples
            var notificationSource = new SingleBlockNotificationStream(aSampleSource);
            //pass the intercepted samples as input data to the spectrumprovider (which will calculate a fft based on them)
            notificationSource.SingleBlockRead += (s, a) => spectrumProvider.Add(a.Left, a.Right);

            _source = notificationSource.ToWaveSource(16);

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            Stop();
        }

        private void Stop()
        {
            timer1.Stop();

            if (_soundOut != null)
            {
                _soundOut.Stop();
                _soundOut.Dispose();
                _soundOut = null;
            }
            if (_soundIn != null)
            {
              _soundIn.Stop();
              _soundIn.Dispose();
              _soundIn = null;
            }
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }


        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //render the spectrum
            GenerateLineSpectrum();
            GenerateVoice3DPrintSpectrum();   
        }

        private void GenerateLineSpectrum()
        {
            Image image = pictureBoxTop.Image;
            var newImage = _lineSpectrum.CreateSpectrumLine(pictureBoxTop.Size, Color.Green, Color.Red, Color.Black, true);
            if (newImage != null)
            {
                pictureBoxTop.Image = newImage;
                if (image != null)
                    image.Dispose();
            }
        }

        private void GenerateVoice3DPrintSpectrum()
        {
            using (Graphics g = Graphics.FromImage(_bitmap))
            {
                pictureBoxBottom.Image = null;
                if (_voicePrint3DSpectrum.CreateVoicePrint3D(g, new RectangleF(0, 0, _bitmap.Width, _bitmap.Height),
                    _xpos, Color.Black, 3))
                {
                    _xpos += 3;
                    if (_xpos >= _bitmap.Width)
                        _xpos = 0;
                }
                pictureBoxBottom.Image = _bitmap;
            }
        }

        private void pitchShiftToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form form = new Form()
            {
                Width = 250,
                Height = 70,
                Text = String.Empty
            };
            TrackBar trackBar = new TrackBar()
            {
                TickStyle = TickStyle.None,
                Minimum = -100,
                Maximum = 100,
                Value = (int) (_pitchShifter != null ? Math.Log10(_pitchShifter.PitchShiftFactor) / Math.Log10(2) * 120 : 0),
                Dock = DockStyle.Fill
            };
            trackBar.ValueChanged += (s, args) =>
            {
                if (_pitchShifter != null)
                {
                    _pitchShifter.PitchShiftFactor = (float) Math.Pow(2, trackBar.Value / 120.0);
                    form.Text = trackBar.Value.ToString();
                }
            };
            form.Controls.Add(trackBar);

            form.ShowDialog();

            form.Dispose();
        }

        private void pictureBoxBottom_Click(object sender, EventArgs e)
        {

        }

        private void pictureBoxTop_Click(object sender, EventArgs e)
        {

        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            click = 0;
            Stop();
        }
    }
}
