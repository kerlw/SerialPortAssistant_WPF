using System.IO.Ports;

namespace MotorController {
    class SerialParamters {
        public string Com { get; set; } //可用串口

        // public string com1 { get; set; } //可用串口

        public string BaudRate { get; set; } //波特率
        public string Parity { get; set; } //校验位
        public Parity ParityValue { get; set; } //校验位对应值
        public string DataBits { get; set; } //数据位
        public string StopBits { get; set; } //停止位
        
        public StopBits StopBitsValue { get; set; }
        public string RecUnicode { get; set; } //接收字符编码
        public string SendUnicode { get; set; } //发送字符编码
    }
}