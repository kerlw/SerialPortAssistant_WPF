using System;
using System.Collections.Generic;
using System.Timers;
using Microsoft.ClearScript.JavaScript;

namespace MotorController {
    public class AutoSendTask {
        /// <summary>
        /// TaskId从1开始，不重复，0表示无效任务
        /// </summary>
        public int Id { get; }

        public bool Repeat { get; private set; } = false;

        public bool Started { get; private set; }

        public bool Finished {
            get => !Repeat && _currentContentIndex >= _contents.Count;
        }

        /// <summary>
        /// 定时任务时间间隔
        /// </summary>
        public int Interval { get; private set; } = 1000;

        /// <summary>
        /// 该任务必须在AfterTask标定的任务完成后开始执行
        /// </summary>
        public int AfterTask { get; private set; } = 0;

        private List<ITypedArray<byte>> _contents = new List<ITypedArray<byte>>();

        private int _currentContentIndex = 0;

        private int _elapsed = 0;

        private AutoSendTask() {
        }

        public AutoSendTask(int id, int interval, ITypedArray<byte> bytes) {
            Id = id;
            Interval = interval;
            _contents.Add(bytes);
        }

        public AutoSendTask(int id, int interval, ITypedArray<byte> bytes, int afterTask) {
            Id = id;
            Interval = interval;
            _contents.Add(bytes);
            AfterTask = afterTask;
        }

        public void Start() {
            Started = true;
        }

        public void Elapse(int elapse) {
            _elapsed += elapse;
        }

        public bool ShouldTrigger() {
            return _elapsed >= Interval;
        }

        public byte[] GetCurrentContent() {
            int length = _contents.Count;
            var content = _contents[(_currentContentIndex++ % length)];
            return content.GetBytes();
        }

        public void ResetElapse() {
            _elapsed = 0;
        }

        public int AppendContent(ITypedArray<byte> content) {
            _contents.Add(content);

            return _contents.Count;
        }

        public void SetRepeat(bool value) {
            Repeat = value;
        }
    }

    public delegate int AutoSendHandler(byte[] content);

    public class ScriptManager {
        static readonly int DefaultTimerInterval = 1000;

        private Dictionary<int, AutoSendTask> _tasks = new Dictionary<int, AutoSendTask>();

        private Timer _timer = null;

        public AutoSendHandler DoSend;

        private int _seqNo = 1;

        // public void OnDataReceived(byte[] bytes) {
        // }

        /// <summary>
        /// 创建一个自动发送任务
        /// </summary>
        /// <param name="interval">时间间隔</param>
        /// <param name="content">发送的内容</param>
        /// <returns>任务的Id</returns>
        public int CreateTask(int interval, ITypedArray<byte> content) {
            var task = new AutoSendTask(_seqNo, interval, content);
            _seqNo++;
            _tasks.Add(task.Id, task);
            return task.Id;
        }

        public int CreateTask(int interval, ITypedArray<byte> content, int afterTask) {
            var task = new AutoSendTask(_seqNo, interval, content, afterTask);
            _seqNo++;
            _tasks.Add(task.Id, task);
            return task.Id;
        }

        /// <summary>
        /// 移除已经添加到队列的任务
        /// </summary>
        /// <param name="id">要移除的任务Id</param>
        /// <returns>成功移除的任务Id</returns>
        public int StopTask(int id) {
            if (_tasks.ContainsKey(id) && _tasks.Remove(id)) {
                return id;
            }

            return 0;
        }

        /// <summary>
        /// 结束所有的任务
        /// </summary>
        public void StopAllTasks() {
            _timer = null;
            _tasks.Clear();
        }

        public int AppendTasksContent(int id, ITypedArray<byte> content) {
            if (_tasks.ContainsKey(id)) {
                return _tasks[id].AppendContent(content);
            }

            return 0;
        }

        public void SetTaskRepeat(int id, bool value) {
            if (_tasks.ContainsKey(id)) {
                _tasks[id].SetRepeat(value);
            }
        }

        /// <summary>
        /// 设定指定Id的任务状态为开始执行
        /// </summary>
        /// <param name="id">任务id</param>
        /// <returns></returns>
        public bool StartTask(int id) {
            if (!_tasks.ContainsKey(id)) {
                return false;
            }

            _tasks[id].Start();
            if (_timer == null) {
                _timer = new Timer();
                _timer.Interval = DefaultTimerInterval;
                _timer.Elapsed += OnElapsed;
                _timer.Start();
            }

            return true;
        }

        private void OnElapsed(object sender, ElapsedEventArgs e) {
            foreach (var task in _tasks.Values) {
                if (task.Started && !task.Finished) {
                    task.Elapse(DefaultTimerInterval);
                    if (task.ShouldTrigger()) {
                        triggerSend(task.GetCurrentContent());
                        task.ResetElapse();

                        if (task.Finished) {
                            //TODO 激活其他绑定到该任务结束时启动的任务
                        }
                    }
                }
            }
        }

        private void triggerSend(byte[] bytes) {
            Console.WriteLine("trigger Send");
            if (DoSend != null) {
                Console.WriteLine("Sending " + bytes.Length + " bytes.");
                DoSend.Invoke(bytes);
            }
        }

        public int Write(ITypedArray<byte> content) {
            var bytes = content.GetBytes();
            if (DoSend != null) {
                return DoSend.Invoke(bytes);
            }

            return 0;
        }
    }
}