//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Microsoft.ML.Runtime.Internal.OpenCV
{
    public unsafe static class OpenCVImports
    {
        private const string DllName = @"OpenCVNative";

        public struct Mat
        {
            public void* Handle;
        }

#pragma warning disable TLC_GeneralName // Externs follow different results
        // REVIEW sptiwari: The pattern of having a private extern PInvoke method and a passthrough public method
        // to expose as API, is used in this class to resolve flags from the FXCop security tool. Specifically, this:
        // Critical Error,CA1401, P/Invokes should not be visible.

        [DllImport(DllName)]
        private static extern Mat Mat_Create_Native(int type, int channels, int rows, int cols, void* data);

        public static Mat Mat_Create(int type, int channels, int rows, int cols, void* data)
        {
            return Mat_Create_Native(type, channels, rows, cols, data);
        }

        [DllImport(DllName)]
        private static extern void Mat_Destroy_Native(Mat handle);

        public static void Mat_Destroy(Mat handle)
        {
            Mat_Destroy_Native(handle);
        }

        [DllImport(DllName)]
        private static extern int Mat_GetRows_Native(Mat handle);

        public static int Mat_GetRows(Mat handle)
        {
            return Mat_GetRows_Native(handle);
        }

        [DllImport(DllName)]
        private static extern int Mat_GetColumns_Native(Mat handle);

        public static int Mat_GetColumns(Mat handle)
        {
            return Mat_GetColumns_Native(handle);
        }

        [DllImport(DllName)]
        private static extern byte* Mat_GetData_Native(Mat handle);

        public static byte* Mat_GetData(Mat handle)
        {
            return Mat_GetData_Native(handle);
        }

        [DllImport(DllName)]
        private static extern int Mat_GetDims_Native(Mat handle);

        public static int Mat_GetDims(Mat handle)
        {
            return Mat_GetDims_Native(handle);
        }

        [DllImport(DllName)]
        private static extern void Mat_GetSteps_Native(Mat handle, int[] steps);

        public static void Mat_GetSteps(Mat handle, int[] steps)
        {
            Mat_GetSteps_Native(handle, steps);
        }

        [DllImport(DllName)]
        private static extern int Mat_GetType_Native(Mat handle);

        public static int Mat_GetType(Mat handle)
        {
            return Mat_GetType_Native(handle);
        }

        [DllImport(DllName)]
        private static extern int Mat_GetChannels_Native(Mat handle);

        public static int Mat_GetChannels(Mat handle)
        {
            return Mat_GetChannels_Native(handle);
        }

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        private static extern Mat Mat_ReadImage_Native(string filename);

        public static Mat Mat_ReadImage(string filename)
        {
            return Mat_ReadImage_Native(filename);
        }

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        private static extern void Mat_SaveImage_Native(Mat handle, string filename);

        public static void Mat_SaveImage(Mat handle, string filename)
        {
            Mat_SaveImage_Native(handle, filename);
        }

        [DllImport(DllName)]
        private static extern Mat Mat_ResizeSize_Native(Mat handle, int width, int height);

        public static Mat Mat_ResizeSize(Mat handle, int width, int height)
        {
            return Mat_ResizeSize_Native(handle, width, height);
        }

        [DllImport(DllName)]
        private static extern Mat Mat_ResizeFactors_Native(Mat handle, double fx, double fy);

        public static Mat Mat_ResizeFactors(Mat handle, double fx, double fy)
        {
            return Mat_ResizeFactors_Native(handle, fx, fy);
        }

        [DllImport(DllName)]
        private static extern Mat Mat_ClipRect_Native(Mat handle, int x, int y, int width, int height);

        public static Mat Mat_ClipRect(Mat handle, int x, int y, int width, int height)
        {
            return Mat_ClipRect_Native(handle, x, y, width, height);
        }

        [DllImport(DllName)]
        private static extern Mat Mat_Pad_Native(Mat handle, int x, int y, int width, int height);

        public static Mat Mat_Pad(Mat handle, int x, int y, int width, int height)
        {
            return Mat_Pad_Native(handle, x, y, width, height);
        }
#pragma warning restore TLC_GeneralName
    }
}
