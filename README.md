# 串口调试助手  #

## 1. 开发背景 ##

Windows应用商店中的串口调试助手可以加载一些脚本来处理接收和自动回复，但是居然要购买pro版本才
能够使用，唉，不就是一个加载js脚本的功能吗，何必呢，只好自己动手写一个了。

基于WPF的现成的串口调试工具在github上找了一下，看到这个[SerialPort.Metro](https://github.com/veryxs/SerialPort.Metro)
觉得看起来还不错，是一个2020年毕业的大学生毕业设计作品，拉下来一看代码写的有点惨不忍睹，只好强
忍着保持大部分原样的东西，然后加上ClearScript.V8的支持来运行脚本。对WPF也不熟悉，ClearScript
更是搜索所得，文档感觉严重不足，临时拼凑的东东，自己的代码也写得惨不忍睹~！：P 暂时满足了当下的
需求，还未完全完善，以后有时间再整理完善吧。

大致的原理是在串口接收到数据的时候，触发js脚本调用，将接收到的数据传递给js，同时注入一个ScriptManager
对象到js层，使js层可以通过ScriptManager来进行一些任务调度（实现接收到某些指令的情况下触发自动定时回复）。

## 2. 在js脚本中注入了以下类型 ## 

* MessageBox: 可以直接使用.Net中的MessageBox来弹出对话框进行提示
* Console: 可以直接使用.Net中的Console来打印日志辅助调试

## 3. 在JS脚本中注入的全局对象 ##

* SourceDataBuffer 接收到的字节数组
* ScriptManager 提供发送、任务相关接口

### ScriptManager.Write(buffer) ###

直接发送buffer作为响应(按职责划分来说应该将这一功能定义在另一个对象中，因为就这一个单一功能，所以偷懒没有定
义新对象，直接放在ScriptManager中了)

### ScriptManager.CreateTask(interval, buffer) ###

创建一个定时发送任务

* interval 定时间隔，单位ms
* buffer 任务将要发送的byte数组
* 返回 创建的任务Id

### ScriptManager.AppendTasksContent(taskId, buffer) ###

向指定Id的定时任务追加一组发送字节

* taskId 任务Id，由CreateTask返回
* buffer 另一组用于定时发送的byte数组（追加到任务的buffer会形成一个队列，每次定时器触发，按顺序取出其中一 条进行发送)

### ScriptManager.StartTask(taskId) ###

启动指定Id的任务

### ScriptManager.SetTaskRepeat(taskId, isRepeatable) ###

设定指定的任务是否重复进行，默认情况下不重复

* taskId 任务Id，由CreateTask返回
* isRepeatable 任务是否重复

### ScriptManager.StopTask(taskId) ###

关闭指定的定时任务

### ScriptManager.StopAllTasks() ###

关闭所有的定时任务


## 4. TODO ##
* [ ] 任务支持队列，即某个任务可以指定在另一个任务完成之后再开始调度
* [ ] 自动响应发送的内容也在当前的“接收区”（应改为“通讯区”）中去显示出来
* [ ] WindowsCommand的 设置 实现Theme的切换

暂时就这样吧，凑活着用~:)

