using System;
using Microsoft.ClearScript.JavaScript;

namespace MotorController {
    public class ByteBuffer : IArrayBuffer {
        private byte[] _buffer;

        public ByteBuffer(byte[] bytes) {
        }

        public byte[] GetBytes() {
            throw new NotImplementedException();
        }

        public ulong ReadBytes(ulong offset, ulong count, byte[] destination, ulong destinationIndex) {
            throw new NotImplementedException();
        }

        public ulong WriteBytes(byte[] source, ulong sourceIndex, ulong count, ulong offset) {
            throw new NotImplementedException();
        }

        public void InvokeWithDirectAccess(Action<IntPtr> action) {
            throw new NotImplementedException();
        }

        public T InvokeWithDirectAccess<T>(Func<IntPtr, T> func) {
            throw new NotImplementedException();
        }

        public ulong Size {
            get => (ulong)_buffer.Length;
        }
    }
}