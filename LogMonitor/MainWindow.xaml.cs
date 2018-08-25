using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace LogMonitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Monitor monitor;

        private bool canStart;

        public MainWindow()
        {
            InitializeComponent();

            OutputScrollViewer.ScrollToEnd();

            monitor = new Monitor(Output);
            canStart = !monitor.IsRunning;

            monitor.StartRunning += delegate
            {
                canStart = false;
                Application.Current.Dispatcher.Invoke(new Action(() => ToggleMonitor.Content = "Stop"));
            };

            monitor.StopRunning += delegate
            {
                canStart = true;
                Application.Current.Dispatcher.Invoke(new Action(() => ToggleMonitor.Content = "Start"));
            };
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".txt",
                Filter = "txt Files (*.txt)|*.log|log Files (*.log)|*.log|All Files (*.*)|*.*"
            };

            // Display OpenFileDialog by calling ShowDialog method 
            var result = dlg.ShowDialog();
            
            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                FileName.Text = dlg.FileName;
            }
        }

        private void ToggleMonitor_Click(object sender, RoutedEventArgs e)
        {
            if(canStart)
                monitor.Start(FileName.Text);
            else
                monitor.Stop();
        }

        private class Monitor
        {
            public event EventHandler<EventArgs>
                StartRunning,
                StopRunning;

            public bool IsRunning { get; internal set; } = false;

            private readonly TextBox output;
            private LogWorker worker;
            
            public Monitor(TextBox output)
            {
                this.output = output;

                StartRunning += delegate { IsRunning = true; };
                StopRunning += delegate { IsRunning = false; };
            }

            public void Start(string fileName)
            {
                #region Early Termination Checks
                if (string.IsNullOrWhiteSpace(fileName))
                    return;

                FileInfo temp;

                try
                {
                    temp = new FileInfo(fileName);
                }
                catch(Exception)
                {
                    MessageBox.Show(messageBoxText: "File Name is Not Valid", caption: "Malformed File Name", button: MessageBoxButton.OK, icon: MessageBoxImage.Error);
                    return;
                }

                if(!temp.Exists)
                {
                    MessageBox.Show(messageBoxText: "Provided File Name Could Not Be Located", caption: "File Not Found", button: MessageBoxButton.OK, icon: MessageBoxImage.Error);
                    return;
                }
                #endregion

                worker = new LogWorker(fileName);

                worker.MonitorStopped += delegate
                {
                    StopRunning?.Invoke(this, new EventArgs());
                };

                worker.OutputUpdate += (obj, args) =>
                {
                    Application.Current.Dispatcher.Invoke(new Action(() => output.Text = args.Output));
                };
                
                worker.RunWorkerAsync();

                StartRunning?.Invoke(this, new EventArgs());

            }

            public void Stop() => worker.RequestStop();

            private class LogWorker : BackgroundWorker
            {


                public event EventHandler<EventArgs>
                    MonitorStarted,
                    MonitorCycleStarted,
                    MonitorStopped;

                public event EventHandler<OutputEventArgs>
                    OutputUpdate;

                private volatile bool requestCancellation = false;

                private FileInfo file;
                private readonly Queue<string> liveLines = new Queue<string>();

                public LogWorker(string fileName)
                {
                    file = new FileInfo(fileName);
                }

                public void RequestStop()
                {
                    lock (this)
                        requestCancellation = true;
                    
                }

                protected override void OnDoWork(DoWorkEventArgs e)
                {
                    base.OnDoWork(e);

                    MonitorStarted?.Invoke(this, new EventArgs());

                    bool _requestCancellation;
                    var output = string.Empty;
                    do
                    {
                        MonitorCycleStarted?.Invoke(this, new EventArgs());

                        #region Update Monitor Output

                        var reader = 
                            new StreamReader(
                            new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                        while(!reader.EndOfStream)
                        {
                            liveLines.Enqueue(reader.ReadLine());
                            while (liveLines.Count > Properties.Settings.Default.MaxDisplayedLines)
                                liveLines.Dequeue();
                        }

                        var sb = new StringBuilder();
                        for(int i = 0; i < liveLines.Count; i++)
                        {
                            var line = liveLines.Dequeue();
                            sb.AppendLine(line);
                            liveLines.Enqueue(line);
                        }

                        var formatted = sb.ToString();
                        if (output != formatted)
                        {
                            output = formatted;



                            OutputUpdate?.Invoke(this, new OutputEventArgs(output));
                        }
                        #endregion

                        lock (this)
                            _requestCancellation = requestCancellation;
                    } while (!_requestCancellation);

                    MonitorStopped?.Invoke(this, new EventArgs());
                }
            }

            public class OutputEventArgs : EventArgs
            {
                public readonly string Output;
                public OutputEventArgs(string output) => Output = output;
            }
        }
    }
}
