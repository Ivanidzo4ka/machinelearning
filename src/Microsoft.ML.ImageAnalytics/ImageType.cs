// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using Microsoft.ML.Runtime.Data;

namespace Microsoft.ML.Runtime.ImageAnalytics
{
    public sealed class ImageType_Bitmap: StructuredType
    {
        public readonly int Height;
        public readonly int Width;
        public ImageType_Bitmap(int height, int width)
           : base(typeof(Bitmap))
        {
            Contracts.CheckParam(height > 0, nameof(height));
            Contracts.CheckParam(width > 0, nameof(width));
            Contracts.CheckParam((long)height * width <= int.MaxValue / 4, nameof(height), "height * width is too large");
            Height = height;
            Width = width;
        }

        public ImageType_Bitmap()
            : base(typeof(System.Drawing.Image))
        {
        }

        public override bool Equals(ColumnType other)
        {
            if (other == this)
                return true;
            var tmp = other as ImageType_Bitmap;
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

   
}
