using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using Microsoft.ClearScript.V8;

namespace MotorController {
    class ScriptItem {
        public string FileName { get; set; }

        public DateTime LastModify { get; set; }
    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : MetroWindow {
        SerialPort _comPort = new SerialPort();

        // 可用串口数组
        private string[] _ports;

        // 接收状态字
        private bool _recStatus = true;

        // COM口开启状态字，在打开/关闭串口中使用，这里没有使用自带的ComPort.IsOpen，因为在串口突然丢失的时候，ComPort.IsOpen会自动false，逻辑混乱
        private bool _isComPortOpen = false;

        // 用于检测是否没有执行完invoke相关操作，仅在单线程收发使用，但是在公共代码区有相关设置，所以未用#define隔离
        private volatile bool _receiving = false;

        // invoke里判断是否正在关闭串口是否正在关闭串口，执行Application.DoEvents，并阻止再次invoke,
        // 解决关闭串口时，程序假死，具体参见http://news.ccidnet.com/art/32859/20100524/2067861_4.html 
        // 仅在单线程收发使用，但是在公共代码区有相关设置，所以未用#define隔离
        private volatile bool _waitClose = false;

        // 可用串口集合
        List<SerialParamters> _comList = new List<SerialParamters>();

        // 定时发送
        DispatcherTimer _autoSendTick = new DispatcherTimer();

        private ObservableCollection<ScriptItem> _scripts = new ObservableCollection<ScriptItem>();

        private ObservableCollection<string> _scriptNames = new ObservableCollection<string>();

        private string _selectedScriptPath = null;

        private int _tabSelectedIndex = -1;

        // public string scriptName { get; set; }
        private V8ScriptEngine _scriptEngine = new V8ScriptEngine();

        private string _injectJs = "";

        private ScriptManager _scriptManager = new ScriptManager();

        public MainWindow() {
            InitializeComponent();
            Loaded += Window_loaded;
        }

        //关闭窗口closing
        private void MetroWindow_Closing(object sender, CancelEventArgs e) {
            MessageBoxResult result = MessageBox.Show("确认是否要退出？", "退出", MessageBoxButton.YesNo); //显示确认窗口
            if (result == MessageBoxResult.No) {
                e.Cancel = true; //取消操作
            }
        }

        //确认关闭后
        private void MetroWindow_Closed(object sender, EventArgs e) {
            Application.Current.Shutdown(); //先停止线程,然后终止进程.
            Environment.Exit(0);            //直接终止进程.
        }

        //主窗口初始化
        private void Window_loaded(object sender, RoutedEventArgs e) {
            listBoxScripts.ItemsSource = _scripts;
            cmbScirpts.ItemsSource = _scriptNames;
            _scriptManager.DoSend += SendToSerial;

            RefreshPortList();
            RefreshScriptsList();
            RefreshScriptNameList();

            //波特率下拉控件
            List<SerialParamters> rateList = new List<SerialParamters> {
                new SerialParamters { BaudRate = "1200" },
                new SerialParamters { BaudRate = "2400" },
                new SerialParamters { BaudRate = "4800" },
                new SerialParamters { BaudRate = "9600" },
                new SerialParamters { BaudRate = "14400" },
                new SerialParamters { BaudRate = "19200" },
                new SerialParamters { BaudRate = "28800" },
                new SerialParamters { BaudRate = "38400" },
                new SerialParamters { BaudRate = "57600" },
                new SerialParamters { BaudRate = "74880" },
                new SerialParamters { BaudRate = "115200" }
            }; //可用波特率集合
            cmbRateList.ItemsSource = rateList;
            cmbRateList.DisplayMemberPath = "BaudRate"; //显示出来的值
            cmbRateList.SelectedValuePath = "BaudRate"; //选中后获取的实际结果


            //数据位下拉控件
            List<SerialParamters> dataBits = new List<SerialParamters> {
                new SerialParamters { DataBits = "8" },
                new SerialParamters { DataBits = "7" },
                new SerialParamters { DataBits = "6" }
            }; //数据位集合
            cmbDataBits.ItemsSource = dataBits;
            cmbDataBits.SelectedValuePath = "DataBits";
            cmbDataBits.DisplayMemberPath = "DataBits";

            //停止位下拉控件
            List<SerialParamters> sbs = new List<SerialParamters> {
                new SerialParamters { StopBits = "None", StopBitsValue = StopBits.None },
                new SerialParamters { StopBits = "One", StopBitsValue = StopBits.One },
                new SerialParamters { StopBits = "Two", StopBitsValue = StopBits.Two },
                new SerialParamters { StopBits = "OnePointFive", StopBitsValue = StopBits.OnePointFive }
            }; //停止位集合
            cmbStopBits.ItemsSource = sbs;
            cmbStopBits.DisplayMemberPath = "StopBits";
            cmbStopBits.SelectedValuePath = "StopBitsValue";

            //校检位下拉控件
            List<SerialParamters> comParity = new List<SerialParamters> {
                new SerialParamters { Parity = "None", ParityValue = Parity.None },
                new SerialParamters { Parity = "Odd", ParityValue = Parity.Odd },
                new SerialParamters { Parity = "Even", ParityValue = Parity.Even },
                new SerialParamters { Parity = "Mark", ParityValue = Parity.Mark },
                new SerialParamters { Parity = "Space", ParityValue = Parity.Space }
            }; //可用校验位集合
            cmbParityCom.ItemsSource = comParity;
            cmbParityCom.DisplayMemberPath = "Parity";
            cmbParityCom.SelectedValuePath = "ParityValue";

            //接收字符编码下拉控件
            List<SerialParamters> recCode = new List<SerialParamters> {
                new SerialParamters { RecUnicode = "UTF-8" },
                new SerialParamters { RecUnicode = "ASCII" },
                new SerialParamters { RecUnicode = "GB2312" },
                new SerialParamters { RecUnicode = "GBK" },
                new SerialParamters { RecUnicode = "Unicode" }
            };
            cmbRecUnicode.ItemsSource = recCode;
            cmbRecUnicode.DisplayMemberPath = "RecUnicode";
            cmbRecUnicode.SelectedValuePath = "RecUnicode";

            //发送字符编码下拉控件
            List<SerialParamters> sendCode = new List<SerialParamters> {
                new SerialParamters { SendUnicode = "UTF-8" },
                new SerialParamters { SendUnicode = "ASCII" },
                new SerialParamters { SendUnicode = "GB2312" },
                new SerialParamters { SendUnicode = "GBK" },
                new SerialParamters { SendUnicode = "Unicode" }
            };
            cmbSendUnicode.ItemsSource = sendCode;
            cmbSendUnicode.DisplayMemberPath = "SendUnicode";
            cmbSendUnicode.SelectedValuePath = "SendUnicode";


            //默认值设置
            cmbRateList.SelectedValue = "115200";     //波特率默认设置9600
            cmbParityCom.SelectedValue = "0";         //校验位默认设置值为0，对应NONE
            cmbDataBits.SelectedValue = "8";          //数据位默认设置8位
            cmbStopBits.SelectedValue = StopBits.One; //停止位默认设置1
            cmbRecUnicode.SelectedValue = "UTF-8";    //接收默认字符为UTF-8
            cmbSendUnicode.SelectedValue = "UTF-8";   //发送默认字符为UTF-8

            _comPort.ReadTimeout = 8000;      //串口读超时8秒
            _comPort.WriteTimeout = 8000;     //串口写超时8秒，在1ms自动发送数据时拔掉串口，写超时5秒后，会自动停止发送，如果无超时设定，这时程序假死
            _comPort.ReadBufferSize = 4096;   //数据读缓存
            _comPort.WriteBufferSize = 4096;  //数据写缓存
            btnSend.IsEnabled = false;        //发送按钮初始化为不可用状态
            chbHexSendMode.IsChecked = false; //发送模式默认为未选中状态
            chbHexMode.IsChecked = false;     //接收模式默认为未选中状态
            //↑↑↑↑↑↑↑↑↑默认设置↑↑↑↑↑↑↑↑↑
            _comPort.DataReceived += ComReceiveHandler; //串口接收中断
            _autoSendTick.Tick += AutoSend;             //定时发送中断
        }

        private int SendToSerial(byte[] content) {
            if (_comPort.IsOpen) {
                _comPort.Write(content, 0, content.Length);
                return content.Length;
            }

            return 0;
        }

        private async void Button_Open(object sender, RoutedEventArgs e) {
            if (cmbAvailableComPorts.SelectedValue == null) {
                MessageBox.Show("无法打开串口", "提示");
                return;
            }

            #region 打开串口

            if (_isComPortOpen == false) {
                //尝试打开串口
                try {
                    //设置要打开的串口
                    _comPort.PortName = cmbAvailableComPorts.SelectedValue.ToString();

                    //设置当前波特率
                    _comPort.BaudRate = Convert.ToInt32(cmbRateList.SelectedValue);
                    //设置当前校验位
                    _comPort.Parity = (Parity)Convert.ToInt32(cmbParityCom.SelectedValue);
                    //设置当前数据位
                    _comPort.DataBits = Convert.ToInt32(cmbDataBits.SelectedValue);
                    //设置当前停止位   
                    _comPort.StopBits = (StopBits)Convert.ToInt32(cmbStopBits.SelectedValue);
                    _comPort.DataReceived += ComReceiveHandler;

                    _comPort.Open(); //打开串口
                } catch {
                    //如果串口被其他占用，则无法打开
                    MessageBox.Show("无法打开串口,请检测此串口是否有效或被其他占用！", "提示");
                    // GetPort();//刷新当前可用串口
                    return; //无法打开串口，提示后直接返回
                }

                //成功打开串口后的设置
                btnOpen.Content = "关闭串口";               // 按钮显示改为“关闭按钮” 
                _isComPortOpen = true;                  // 串口打开状态字改为true
                _waitClose = false;                     // 等待关闭串口状态改为false                
                btnSend.IsEnabled = true;               // 使能“发送数据”按钮
                btnDefaultSet.IsEnabled = false;        // 打开串口后失能重置功能
                cmbAvailableComPorts.IsEnabled = false; // 失能可用串口控件
                cmbRateList.IsEnabled = false;          // 失能可用波特率控件
                cmbParityCom.IsEnabled = false;         // 失能可用校验位控件
                cmbDataBits.IsEnabled = false;          // 失能可用数据位控件
                cmbStopBits.IsEnabled = false;          // 失能可用停止位控件
                btnRefreshPorts.IsEnabled = false;      // 失能刷新串口按钮控件

                //如果打开前，自动发送控件就被选中，则打开串口后自动开始发送数据
                if (chbAutoSend.IsChecked == true) {
                    _autoSendTick.Interval = TimeSpan.FromMilliseconds(Convert.ToInt32(Time.Text)); //设置自动发送间隔
                    _autoSendTick.Start();                                                          //开启自动发送
                }
            }

            #endregion

            #region 关闭串口

            //ComPortIsOpen == true,当前串口为打开状态，按钮事件为关闭串口
            else {
                // 避免重复调用
                if (_waitClose)
                    return;

                //尝试关闭串口
                try {
                    //激活正在关闭状态字，用于在串口接收方法的invoke里判断是否正在关闭串口
                    _waitClose = true;

                    _comPort.DataReceived -= ComReceiveHandler;

                    _scriptManager.StopAllTasks();
                    _autoSendTick.Stop();          //停止自动发送
                    chbAutoSend.IsChecked = false; //停止自动发送控件改为未选中状态
                    _comPort.DiscardOutBuffer();   //清发送缓存
                    _comPort.DiscardInBuffer();    //清接收缓存

                    //判断invoke是否结束
                    while (_receiving) {
                        await Dispatcher.Yield();
                    }

                    _comPort.Close(); //关闭串口
                    _waitClose = false;
                    SetAfterClose(); //成功关闭串口或串口丢失后的设置
                } catch {
                    //如果在未关闭串口前，串口就已丢失，这时关闭串口会出现异常

                    //判断当前串口状态，如果ComPort.IsOpen==false，说明串口已丢失
                    if (_comPort.IsOpen == false) {
                        SetComLose();
                    } else {
                        //未知原因，无法关闭串口
                        MessageBox.Show("无法关闭串口，原因未知！", "提示");
                    }
                }
            }

            #endregion
        }

        private void SendBtn_Click(object sender, RoutedEventArgs e) {
            ComSend();
        }

        //自动发送
        void AutoSend(object sender, EventArgs e) {
            ComSend();
        }


        // //模拟 Winfrom 中 Application.DoEvents() 详见 http://www.silverlightchina.net/html/study/WPF/2010/1216/4186.html?1292685167
        // public static class DispatcherHelper {
        //     [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        //     public static void DoEvents() {
        //         DispatcherFrame frame = new DispatcherFrame();
        //         Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
        //             new DispatcherOperationCallback(ExitFrames), frame);
        //         try {
        //             Dispatcher.PushFrame(frame);
        //         } catch (InvalidOperationException) {
        //         }
        //     }
        //
        //     private static object ExitFrames(object frame) {
        //         ((DispatcherFrame)frame).Continue = false;
        //         return null;
        //     }
        // }


        //发送数据 普通方法，发送数据过程中UI会失去响应
        private void ComSend() {
            byte[] sendBuffer = null;        //发送数据缓冲区
            string sendData = sendTBox.Text; //复制发送数据，以免发送过程中数据被手动改变

            //16进制发送
            if (chbHexSendMode.IsChecked == true) {
                try {
                    sendData = sendData.Replace(" ", "");  //去除16进制数据中所有空格
                    sendData = sendData.Replace("\r", ""); //去除16进制数据中所有换行
                    sendData = sendData.Replace("\n", ""); //去除16进制数据中所有换行
                    if (sendData.Length == 1) {            //数据长度为1的时候，在数据前补0
                        sendData = "0" + sendData;
                    } else if (sendData.Length % 2 != 0) { //数据长度为奇数位时，去除最后一位数据
                        sendData = sendData.Remove(sendData.Length - 1, 1);
                    }

                    sendBuffer = new byte[sendData.Length / 2];
                    for (int i = 0; i < sendData.Length / 2; i++) {
                        sendBuffer[i] = (byte)Convert.ToInt32(sendData.Substring(i * 2, 2), 16);
                    }
                } catch {
                    chbAutoSend.IsChecked = false; //自动发送改为未选中
                    _autoSendTick.Stop();          //关闭自动发送
                    MessageBox.Show("请输入正确的16进制数据", "提示");
                    return; //输入的16进制数据错误，无法发送，提示后返回
                }
            } else { //ASCII码文本发送
                // sendBuffer = System.Text.Encoding.Default.GetBytes(sendData);//转码
                sendBuffer = Encoding.GetEncoding(cmbSendUnicode.Text).GetBytes(sendData);
            }

            //尝试发送数据
            try {
                //如果发送字节数大于1000，则每1000字节发送一次
                int sendTimes = sendBuffer.Length / 1000;
                for (int i = 0; i < sendTimes; i++) {
                    _comPort.Write(sendBuffer, i * 1000, 1000);
                    //刷新发送字节数
                    sendCount.Text = (Convert.ToInt32(sendCount.Text) + 1000).ToString();
                }

                if (sendBuffer.Length % 1000 != 0) {
                    _comPort.Write(sendBuffer, sendTimes * 1000, sendBuffer.Length % 1000); //发送字节小于1000Bytes或上面发送剩余的数据
                    sendCount.Text = (Convert.ToInt32(sendCount.Text) + sendBuffer.Length % 1000).ToString(); //刷新发送字节数
                }
            } catch {
                //如果ComPort.IsOpen == false，说明串口已丢失
                if (_comPort.IsOpen == false) {
                    SetComLose(); //串口丢失后相关设置
                } else {
                    MessageBox.Show("无法发送数据，原因未知！", "提示");
                }
            }
            //sendScrol.ScrollToBottom();//发送数据区滚动到底部
        }

        //接收数据 数据在接收中断里面处理
        private void ComReceiveHandler(object sender, SerialDataReceivedEventArgs e) {
            //如果正在关闭串口，则直接返回
            if (_waitClose) return;

            //如果已经开启接收
            if (_recStatus) {
                try {
                    _receiving = true; //设置标记，开始处理数据

                    // 简单的使用加延时作为断帧的手段
                    Thread.Sleep(20);
                    // 可能会触发多次事件，而后面的事件中BytesToRead为0
                    if (_comPort.BytesToRead <= 0) {
                        return;
                    }

                    byte[] recBuffer = new byte[_comPort.BytesToRead]; //接收数据缓存
                    _comPort.Read(recBuffer, 0, recBuffer.Length);     //读取数据

                    // 更改UI的操作回归到UI线程进行
                    recTBox.Dispatcher.Invoke(
                        delegate {
                            //接收数据字节数
                            recCount.Text = (Convert.ToInt32(recCount.Text) + recBuffer.Length).ToString();

                            string recData = "";
                            if (chbHexMode.IsChecked == false) { //接收模式为ASCII文本模式
                                recData = Encoding.GetEncoding(cmbRecUnicode.Text).GetString(recBuffer);
                            } else {
                                StringBuilder recBuffer16 = new StringBuilder(); //定义16进制接收缓存
                                foreach (var b in recBuffer) {
                                    recBuffer16.AppendFormat("{0:X2} ", b);
                                }

                                recData = recBuffer16.ToString();
                            }

                            recTBox.Text += "« " + recData + "\n"; //加显到接收区
                            recTBox.ScrollToEnd();

                            // 执行脚本，由于访问了chbExtensionScript属于UI线程资源，故放入invoke中
                            if (chbExtensionScript.IsChecked == true && !String.IsNullOrEmpty(_injectJs)) {
                                //dynamic createJsByteArray = _scriptEngine.Evaluate(@"
                                //    (function (length) {
                                //        return new Uint8Array(length);
                                //    }).valueOf()
                                //");
                                //var array = (ITypedArray<byte>)createJsByteArray(recBuffer.Length);
                                //array.Write(recBuffer, 0, (ulong)recBuffer.Length, 0);
                                //_scriptEngine.AddHostObject("SourceDataBuffer", array);

                                _scriptEngine.AddHostObject("SourceDataBuffer", recBuffer);
                                _scriptEngine.Script.main();
                            }
                        }
                    );
                } finally {
                    //UI使用结束，用于关闭串口时判断，避免自动发送时拔掉串口，陷入死循环
                    _receiving = false;
                }
            } else {
                //暂停接收, 清接收缓存
                _comPort.DiscardInBuffer();
            }
        }


        //成功关闭串口或串口丢失后的设置
        private void SetAfterClose() {
            btnOpen.Content = "打开串口"; //按钮显示为“打开串口”

            _isComPortOpen = false;                //串口状态设置为关闭状态
            btnSend.IsEnabled = false;             //失能发送数据按钮
            btnDefaultSet.IsEnabled = true;        //打开串口后使能重置功能
            cmbAvailableComPorts.IsEnabled = true; //使能可用串口控件
            cmbRateList.IsEnabled = true;          //使能可用波特率下拉控件
            cmbParityCom.IsEnabled = true;         //使能可用校验位下拉控件
            cmbDataBits.IsEnabled = true;          //使能数据位下拉控件
            cmbStopBits.IsEnabled = true;          //使能停止位下拉控件
            btnRefreshPorts.IsEnabled = true;
        }

        //成功关闭串口或串口丢失后的设置
        private async void SetComLose() {
            _autoSendTick.Stop();          //串口丢失后要关闭自动发送
            chbAutoSend.IsChecked = false; //自动发送改为未选中
            _waitClose = true;             //;//激活正在关闭状态字，用于在串口接收方法的invoke里判断是否正在关闭串口
            while (_receiving) {           //判断invoke是否结束
                await Dispatcher.Yield();
            }

            MessageBox.Show("串口已丢失", "提示");
            _waitClose = false; //关闭正在关闭状态字，用于在串口接收方法的invoke里判断是否正在关闭串口
            RefreshPortList();  //刷新可用串口
            SetAfterClose();    //成功关闭串口或串口丢失后的设置
        }

        private void SendClearBtn_Click(object sender, RoutedEventArgs e) {
            sendTBox.Text = "";
        }

        private void RecClearBtn_Click(object sender, RoutedEventArgs e) {
            recTBox.Text = "";
        }

        private void CountClear_Click(object sender, RoutedEventArgs e) {
            sendCount.Text = "0";
            recCount.Text = "0";
        }

        private void StopRecBtn_Click(object sender, RoutedEventArgs e) {
            if (_recStatus) {
                //当前为开启接收状态，暂停接收
                _recStatus = false;
                stopRecBtn.Content = "开启接收";
            } else {
                //当前状态为关闭接收状态，开启接收
                _recStatus = true;
                stopRecBtn.Content = "暂停接收";
            }
        }


        //自动发送控件点击事件
        private void autoSendCheck_Click(object sender, RoutedEventArgs e) {
            //如果当前状态为开启自动发送且串口已打开，则开始自动发送
            if (chbAutoSend.IsChecked == true && _comPort.IsOpen) {
                _autoSendTick.Interval = TimeSpan.FromMilliseconds(Convert.ToInt32(Time.Text)); //设置自动发送间隔
                _autoSendTick.Start();                                                          //开始自动发送定时器
            } else {
                _autoSendTick.Stop(); //关闭自动发送定时器
            }
        }

        //发送周期文本控件-失去事件
        private void Time_LostFocus(object sender, RoutedEventArgs e) {
            //时间为空或时间等于0，设置为1000
            if (Time.Text.Length == 0 || Convert.ToInt32(Time.Text) == 0) {
                Time.Text = "1000";
            }

            _autoSendTick.Interval = TimeSpan.FromMilliseconds(Convert.ToInt32(Time.Text)); //设置自动发送周期
        }

        private void DefaultSet_Click(object sender, RoutedEventArgs e) {
            cmbRateList.SelectedValue = "115200"; //波特率默认设置9600
            cmbParityCom.SelectedValue = "0";     //校验位默认设置值为0，对应NONE
            cmbDataBits.SelectedValue = "8";      //数据位默认设置8位
            cmbStopBits.SelectedValue = "1";      //停止位默认设置1
            RefreshPortList();
        }


        private void tb_PreviewTextInput(object sender, TextCompositionEventArgs e) {
            Regex re = new Regex("[^0-9.\\-]+");
            e.Handled = re.IsMatch(e.Text);
        }

        void TextBoxEx_TextChanged(object sender, TextChangedEventArgs e) {
            //屏蔽中文输入和非法字符粘贴输入
            var textBox = sender as TextBox;
            var change = new TextChange[e.Changes.Count];
            e.Changes.CopyTo(change, 0);


            int offset = change[0].Offset;
            if (change[0].AddedLength > 0) {
                double num;
                if (textBox != null && !Double.TryParse(textBox.Text, out num)) {
                    textBox.Text = textBox.Text.Remove(offset, change[0].AddedLength);
                    textBox.Select(offset, 0);
                }
            }
        }


        void TextBoxEx_KeyDown(object sender, KeyEventArgs e) {
            var txt = sender as TextBox;
            //屏蔽非法按键
            if ((e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) || e.Key == Key.Decimal) {
                if (txt != null && (txt.Text.Contains(".") && e.Key == Key.Decimal)) {
                    e.Handled = true;
                    return;
                }

                e.Handled = false;
            } else if (((e.Key >= Key.D0 && e.Key <= Key.D9) || e.Key == Key.OemPeriod) &&
                       e.KeyboardDevice.Modifiers != ModifierKeys.Shift) {
                if (txt != null && (txt.Text.Contains(".") && e.Key == Key.OemPeriod)) {
                    e.Handled = true;
                    return;
                }

                e.Handled = false;
            } else {
                e.Handled = true;
            }
        }

        private void BtnScriptSave_Click(object sender, RoutedEventArgs e) {
            if (String.IsNullOrEmpty(_selectedScriptPath)) {
                return;
            }

            if (File.Exists(_selectedScriptPath)) {
                File.WriteAllText(_selectedScriptPath, tbScript.Text, Encoding.UTF8);
            }
        }

        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e) {
            RefreshPortList();
        }

        private void RefreshPortList() {
            //可用串口下拉控件
            _ports = SerialPort.GetPortNames(); //获取可用串口
            _comList.Clear();
            if (_ports.Length > 0) {
                for (int i = 0; i < _ports.Length; i++) {
                    _comList.Add(new SerialParamters { Com = _ports[i] }); //下拉控件里添加可用串口
                }

                cmbAvailableComPorts.ItemsSource = _comList;    //资源路径
                cmbAvailableComPorts.DisplayMemberPath = "Com"; //显示路径
                cmbAvailableComPorts.SelectedValuePath = "Com"; //值路径
                cmbAvailableComPorts.SelectedValue = _ports[0]; //默认选择第一个
            }
        }

        private void RefreshScriptsList() {
            _scripts.Clear();

            // 确保scripts目录已创建
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

            DirectoryInfo dinfo = new DirectoryInfo(path);

            var files = dinfo.GetFiles("*.js");
            foreach (var file in files) {
                var name = file.Name.Substring(0, file.Name.LastIndexOf(file.Extension));
                _scripts.Add(new ScriptItem { FileName = name, LastModify = file.LastWriteTime });
            }
        }

        private void RefreshScriptNameList() {
            _scriptNames.Clear();

            // 确保scripts目录已创建
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

            DirectoryInfo dinfo = new DirectoryInfo(path);

            var files = dinfo.GetFiles("*.js");
            foreach (var file in files) {
                var name = file.Name.Substring(0, file.Name.LastIndexOf(file.Extension));
                _scriptNames.Add(name);
            }
        }

        private void BtnAddScript_Click(object sender, RoutedEventArgs e) {
            var scriptName = tbScriptName.Text;
            if (String.IsNullOrEmpty(scriptName)) {
                MessageBox.Show("请先输入脚本名称。");
                return;
            }


            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

            var filepath = Path.Combine(path, scriptName + ".js");
            if (File.Exists(filepath)) {
                MessageBox.Show("已经存在名为 " + scriptName + " 的脚本，请更改脚本名称。");
                return;
            }

            var fmt = "/********************************************\r\n" +
                      " *  脚本:  {0}                             \r\n" +
                      " *  备注:                                     \r\n" +
                      " ********************************************/\r\n" +
                      "function main() {{\r\n" +
                      "}}\r\n";
            var content = String.Format(fmt, scriptName);
            File.WriteAllText(filepath, content, Encoding.UTF8);
            _scripts.Insert(0, new ScriptItem { FileName = scriptName, LastModify = DateTime.Now });
            listBoxScripts.ItemsSource = _scripts;
            listBoxScripts.SelectedIndex = 0;
        }

        private void BtnDeleteScript_Click(object sender, RoutedEventArgs e) {
            if (listBoxScripts.SelectedIndex == -1) {
                MessageBox.Show("请先选择要删除的脚本。");
                return;
            }

            var item = _scripts[listBoxScripts.SelectedIndex];
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", item.FileName + ".js");
            if (File.Exists(path)) {
                File.Delete(path);
                MessageBox.Show("文件已删除。");
            }

            _scripts.Remove(item);
        }

        private void ListBoxScripts_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.Handled = true;
            var item = listBoxScripts.SelectedItem as ScriptItem;
            if (listBoxScripts.SelectedItem != null) {
                _selectedScriptPath =
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", item.FileName + ".js");
                var content = File.ReadAllText(_selectedScriptPath);

                tbScript.Text = content;
            } else {
                _selectedScriptPath = null;
                tbScript.Text = "";
            }
        }

        private void ChbAutoReplyScript_OnCheckedChanged(object sender, RoutedEventArgs e) {
            checkScriptInjection();
        }

        private void CmbScirpts_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.Handled = true;
            checkScriptInjection();
        }

        private void checkScriptInjection() {
            if (chbExtensionScript.IsChecked == true) {
                // scriptEngine.AddHostObject();
                var filename = cmbScirpts.SelectedValue + ".js";
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", filename);
                if (File.Exists(path)) {
                    _injectJs = File.ReadAllText(path);
                    _scriptEngine.AddHostType(typeof(MessageBox));
                    _scriptEngine.AddHostType(typeof(Console));
                    _scriptEngine.AddHostObject("ScriptManager", _scriptManager);
                    _scriptEngine.Execute(_injectJs);
                    return;
                }
            }
            _injectJs = "";
        }

        private void TabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.Handled = true;
            if (_tabSelectedIndex == tabCtrl.SelectedIndex)
                return;

            _tabSelectedIndex = tabCtrl.SelectedIndex;
            switch (tabCtrl.SelectedIndex) {
                case 0:
                    // 没有激活自动应答脚本的时候再刷新脚本列表
                    if (chbExtensionScript.IsChecked != true) {
                        RefreshScriptNameList();
                    }
                    break;
                case 1:
                    RefreshScriptsList();
                    break;
            }
        }

        private void CmdBtnSettings_OnClick(object sender, RoutedEventArgs e) {
            throw new NotImplementedException();
        }
    }
}