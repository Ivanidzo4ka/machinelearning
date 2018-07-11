//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using Microsoft.ML.Runtime.Internal.OpenCV;

namespace Microsoft.ML.Runtime.Data
{
    /// <summary>
    /// The ColumnType for the picture manipulation transforms. Note that this is not a PrimitiveType
    /// since the values need special handling (namely disposing).
    /// </summary>
    public sealed class ImageType : StructuredType
    {
        public readonly int Height;
        public readonly int Width;

        public ImageType(int height, int width)
            : base(typeof(Image))
        {
            Contracts.CheckParam(height > 0, nameof(height));
            Contracts.CheckParam(width > 0, nameof(width));
            Contracts.CheckParam((long)height * width <= int.MaxValue / 4, nameof(height), "height * width is too large");
            Height = height;
            Width = width;
        }

        public ImageType()
            : base(typeof(Image))
        {
        }

        public override bool Equals(ColumnType other)
        {
            if (other == this)
                return true;
            var tmp = other as ImageType;
            if (tmp == null)
                return false;
            if (Height != tmp.Height)
                return false;
            if (Width != tmp.Width)
                return false;
            return true;
        }

        public override string ToString()
        {
            if (Height == 0 && Width == 0)
                return "Picture";
            return string.Format("Picture<{0}, {1}>", Height, Width);
        }
    }

    /// <summary>
    /// A wrapper around the OpenCV Mat type, currently only
    /// really support 2D matrices with U8 or floating point data.
    /// </summary>
    public unsafe sealed class Image
        : IDisposable
    {
        private OpenCVImports.Mat _mat;
        private readonly long _memPressure;
        private readonly int[] _steps;
        private readonly int _dims;

        public enum Types
        {
            CV8U = 0,
            CV32F = 5,
            CV64F = 6
        }

        public byte* RawData => _mat.Handle == null ? null : OpenCVImports.Mat_GetData(_mat);
        public int Rows => _mat.Handle == null ? -1 : OpenCVImports.Mat_GetRows(_mat);
        public int Columns => _mat.Handle == null ? -1 : OpenCVImports.Mat_GetColumns(_mat);
        public int Channels => _mat.Handle == null ? -1 : OpenCVImports.Mat_GetChannels(_mat);
        public Types Type => _mat.Handle == null ? Types.CV8U : (Types)OpenCVImports.Mat_GetType(_mat);
        public OpenCVImports.Mat Handle => _mat;

        public Image(OpenCVImports.Mat inner)
        // This one should be private
        {
            Contracts.Assert(inner.Handle != null);
            _mat = inner;
            _memPressure = Rows * Columns * 3;
            if (_memPressure > 0)
                GC.AddMemoryPressure(_memPressure);

            _dims = OpenCVImports.Mat_GetDims(_mat);
            _steps = new int[_dims];
            OpenCVImports.Mat_GetSteps(_mat, _steps);
        }

        public Image(Types type, int channels, int rows, int cols, void* data)
            : this(OpenCVImports.Mat_Create((int)type, channels, rows, cols, data))
        {
        }

        public Image(String fileName)
            : this(OpenCVImports.Mat_ReadImage(fileName))
        {
        }

        public void* GetAddress(int row, int col)
        {
            return RawData + row * _steps[0] + col * _steps[1];
        }

        ~Image()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_mat.Handle != null)
            {
                OpenCVImports.Mat_Destroy(_mat);
                if (_memPressure > 0)
                    GC.RemoveMemoryPressure(_memPressure);
                _mat.Handle = null;
            }
        }
    }

    public unsafe static class ImageReader
    {
        public static double GetDouble(this Image source, int row, int col, int channel = 0)
        {
            Contracts.Assert(source != null);
            Contracts.Assert(source.Type == Image.Types.CV64F);
            return *((double*)source.GetAddress(row, col) + channel * 8);
        }

        public static float GetFloat(this Image source, int row, int col, int channel = 0)
        {
            Contracts.Assert(source != null);
            Contracts.Assert(source.Type == Image.Types.CV32F);
            return *((float*)source.GetAddress(row, col) + channel * 4);
        }

        public static byte GetByte(this Image source, int row, int col, int channel = 0)
        {
            Contracts.Assert(source != null);
            Contracts.Assert(source.Type == Image.Types.CV8U);
            return *((byte*)source.GetAddress(row, col) + channel);
        }

        public static void SaveAs(this Image source, string fileName)
        {
            OpenCVImports.Mat_SaveImage(source.Handle, fileName);
        }
    }
}
