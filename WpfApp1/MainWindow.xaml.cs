using System;
using System.Windows;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SolderingMonitor
{
    public partial class MainWindow : Window
    {
        private SerialPort serialPort;
        private CancellationTokenSource cancellationTokenSource;
        private ConcurrentQueue<string> dataQueue = new ConcurrentQueue<string>();
        private const string DATA_DELIMITER = ",";
        private Task serialMonitorTask;
        private readonly object serialLock = new object();
        private System.Windows.Threading.DispatcherTimer uiTimer;
        private bool isSendingCommand = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeUiTimer();
            PopulateComPorts();
        }

        private void InitializeUiTimer()
        {
            uiTimer = new System.Windows.Threading.DispatcherTimer();
            uiTimer.Interval = TimeSpan.FromMilliseconds(100);
            uiTimer.Tick += ProcessQueuedData;
            uiTimer.Start();
        }

        private async Task SendCommandAsync(string command)
        {
            if (serialPort?.IsOpen == true && !isSendingCommand)
            {
                try
                {
                    isSendingCommand = true;
                    await Task.Run(() =>
                    {
                        lock (serialLock)
                        {
                            serialPort.WriteLine(command);
                        }
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => 
                        MessageBox.Show($"Error sending command: {ex.Message}"));
                }
                finally
                {
                    isSendingCommand = false;
                }
            }
        }

        private void ProcessQueuedData(object sender, EventArgs e)
        {
            while (dataQueue.TryDequeue(out string data))
            {
                try
                {
                    string[] values = data.Split(DATA_DELIMITER);
                    if (values.Length == 9)
                    {
                        // Update Soldering Iron
                        SolderingIronTemperature.Text = $"{values[0]}°C";
                        SolderingIronPower.Value = double.Parse(values[1]);
                        SolderingIronPowerSlider.Value = double.Parse(values[1]);
                        SolderingIronToggle.IsChecked = values[1] != "0";

                        // Update SMD Rework
                        SMDReworkTemperature.Text = $"{values[2]}°C";
                        SMDReworkPower.Value = double.Parse(values[3]);
                        SMDReworkPowerSlider.Value = double.Parse(values[3]);
                        SMDReworkToggle.IsChecked = values[4] == "1";
                        SMDReworkAirFlow.Value = double.Parse(values[5]);
                        SMDReworkAirFlowSlider.Value = double.Parse(values[5]);

                        // Update LCD Repair
                        LCDRepairTemperature.Text = $"{values[6]}°C";
                        LCDRepairPower.Value = double.Parse(values[7]);
                        LCDRepairPowerSlider.Value = double.Parse(values[7]);
                        LCDRepairToggle.IsChecked = values[7] != "0";
                        VacuumPumpToggle.IsChecked = values[8] == "1";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error processing data: {ex.Message}");
                }
            }
        }

        private async Task MonitorSerialPortAsync(CancellationToken cancellationToken)
        {
            var dataBuffer = new StringBuilder();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (serialPort?.IsOpen == true)
                    {
                        while (serialPort.BytesToRead > 0)
                        {
                            int data = serialPort.ReadByte();
                            if (data == '\n')
                            {
                                string completeData = dataBuffer.ToString().Trim();
                                if (!string.IsNullOrEmpty(completeData))
                                {
                                    dataQueue.Enqueue(completeData);
                                }
                                dataBuffer.Clear();
                            }
                            else
                            {
                                dataBuffer.Append((char)data);
                            }
                        }
                    }
                    await Task.Delay(10, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    await Dispatcher.InvokeAsync(DisconnectSerialPort);
                    break;
                }
            }
        }

        private void PopulateComPorts()
        {
            var portsList = new HashSet<string>();

            // Method 1: SerialPort.GetPortNames()
            try
            {
                var standardPorts = SerialPort.GetPortNames();
                foreach (string port in standardPorts)
                {
                    portsList.Add(port);
                }
            }
            catch { }

            // Method 2: WMI query
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%)'"))
                using (var collection = searcher.Get())
                {
                    foreach (var device in collection)
                    {
                        string caption = device["Caption"]?.ToString();
                        if (!string.IsNullOrEmpty(caption) && caption.Contains("(COM"))
                        {
                            string portName = "COM" + caption.Split('(', ')')[1].Split('M')[1];
                            portsList.Add(portName);
                        }
                    }
                }
            }
            catch { }

            var sortedPorts = portsList.OrderBy(p => p).ToList();
            sortedPorts.Insert(0, "Select COM Port");
            ComPortComboBox.ItemsSource = sortedPorts;
            ComPortComboBox.SelectedIndex = 0;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ComPortComboBox.SelectedIndex == 0)
            {
                MessageBox.Show("Please select a COM port.");
                return;
            }

            if (serialPort != null && serialPort.IsOpen)
            {
                DisconnectSerialPort();
            }
            else
            {
                ConnectSerialPort();
            }
        }

        private void ConnectSerialPort()
        {
            string selectedPort = ComPortComboBox.SelectedItem.ToString();
            try
            {
                lock (serialLock)
                {
                    serialPort = new SerialPort(selectedPort)
                    {
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = Parity.None,
                        StopBits = StopBits.One,
                        ReadTimeout = 500,
                        WriteTimeout = 500
                    };

                    serialPort.Open();
                }

                cancellationTokenSource = new CancellationTokenSource();
                serialMonitorTask = Task.Run(() => MonitorSerialPortAsync(cancellationTokenSource.Token),
                                          cancellationTokenSource.Token);

                ConnectButton.Content = "Disconnect";
                EnableControls(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening COM port: {ex.Message}");
                DisconnectSerialPort();
            }
        }

        private void DisconnectSerialPort()
        {
            try
            {
                cancellationTokenSource?.Cancel();
                serialMonitorTask?.Wait(1000);

                lock (serialLock)
                {
                    if (serialPort != null)
                    {
                        if (serialPort.IsOpen)
                        {
                            serialPort.Close();
                        }
                        serialPort.Dispose();
                        serialPort = null;
                    }
                }

                ConnectButton.Content = "Connect";
                EnableControls(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error closing COM port: {ex.Message}");
            }
            finally
            {
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                serialMonitorTask = null;
            }
        }

        private void EnableControls(bool enabled)
        {
            SolderingIronPowerSlider.IsEnabled = enabled;
            SolderingIronToggle.IsEnabled = enabled;
            SMDReworkPowerSlider.IsEnabled = enabled;
            SMDReworkAirFlowSlider.IsEnabled = enabled;
            SMDReworkToggle.IsEnabled = enabled;
            LCDRepairPowerSlider.IsEnabled = enabled;
            LCDRepairToggle.IsEnabled = enabled;
            VacuumPumpToggle.IsEnabled = enabled;
        }

        // Control Event Handlers
        private async void SolderingIronToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ((ToggleButton)sender).IsChecked ?? false;
            await SendCommandAsync($"SI,PWR,{(isOn ? "1" : "0")}");
        }

        private async void SolderingIronPowerSlider_ValueChanged(object sender, 
            RoutedPropertyChangedEventArgs<double> e)
        {
            await SendCommandAsync($"SI,SET,{(int)e.NewValue}");
        }

        private async void SMDReworkToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ((ToggleButton)sender).IsChecked ?? false;
            await SendCommandAsync($"SMD,PWR,{(isOn ? "1" : "0")}");
        }

        private async void SMDReworkPowerSlider_ValueChanged(object sender, 
            RoutedPropertyChangedEventArgs<double> e)
        {
            await SendCommandAsync($"SMD,SET,{(int)e.NewValue}");
        }

        private async void SMDReworkAirFlowSlider_ValueChanged(object sender, 
            RoutedPropertyChangedEventArgs<double> e)
        {
            await SendCommandAsync($"SMD,AIR,{(int)e.NewValue}");
        }

        private async void LCDRepairToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ((ToggleButton)sender).IsChecked ?? false;
            await SendCommandAsync($"LCD,PWR,{(isOn ? "1" : "0")}");
        }

        private async void LCDRepairPowerSlider_ValueChanged(object sender, 
            RoutedPropertyChangedEventArgs<double> e)
        {
            await SendCommandAsync($"LCD,SET,{(int)e.NewValue}");
        }

        private async void VacuumPumpToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ((ToggleButton)sender).IsChecked ?? false;
            await SendCommandAsync($"LCD,VAC,{(isOn ? "1" : "0")}");
        }

        protected override void OnClosed(EventArgs e)
        {
            uiTimer?.Stop();
            DisconnectSerialPort();
            base.OnClosed(e);
        }
    }
}