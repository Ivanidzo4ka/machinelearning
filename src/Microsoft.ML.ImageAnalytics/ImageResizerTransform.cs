﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Text;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.Internal.Internallearn;
using Microsoft.ML.Runtime.Internal.Utilities;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.Runtime.ImageAnalytics;
using System.Drawing.Drawing2D;

[assembly: LoadableClass(ImageResizer_BitmapTransform.Summary, typeof(ImageResizer_BitmapTransform), typeof(ImageResizer_BitmapTransform.Arguments), typeof(SignatureDataTransform),
    ImageResizer_BitmapTransform.UserName, "ImageResizerTransform", "ImageResizer")]

[assembly: LoadableClass(ImageResizer_BitmapTransform.Summary, typeof(ImageResizer_BitmapTransform), null, typeof(SignatureLoadDataTransform),
    ImageResizer_BitmapTransform.UserName, ImageResizer_BitmapTransform.LoaderSignature)]



namespace Microsoft.ML.Runtime.Data
{
    // REVIEW coeseanu: Rewrite as LambdaTransform to simplify.
    public sealed class ImageResizer_BitmapTransform : OneToOneTransformBase
    {

        public sealed class Column : OneToOneColumn
        {
            [Argument(ArgumentType.AtMostOnce, HelpText = "Width of the resized image", ShortName = "width")]
            public int? ImageWidth;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Height of the resized image", ShortName = "height")]
            public int? ImageHeight;


            public static Column Parse(string str)
            {
                Contracts.AssertNonEmpty(str);

                var res = new Column();
                if (res.TryParse(str))
                    return res;
                return null;
            }

            public bool TryUnparse(StringBuilder sb)
            {
                Contracts.AssertValue(sb);
                if (ImageWidth != null || ImageHeight != null)
                    return false;
                return TryUnparseCore(sb);
            }
        }

        public class Arguments : TransformInputBase
        {
            [Argument(ArgumentType.Multiple | ArgumentType.Required, HelpText = "New column definition(s) (optional form: name:src)", ShortName = "col", SortOrder = 1)]
            public Column[] Column;

            [Argument(ArgumentType.Required, HelpText = "Resized width of the image", ShortName = "width")]
            public int ImageWidth;

            [Argument(ArgumentType.Required, HelpText = "Resized height of the image", ShortName = "height")]
            public int ImageHeight;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Resizing method", ShortName = "scale")]
            public ISupportResizeImageFunctionCombinerFactory ResizeFunction = new ResizeWithPadding.Arguments();
        }

        /// <summary>
        /// Extra information for each column (in addition to ColumnInfo).
        /// </summary>
        private sealed class ColInfoEx
        {
            public readonly int Width;
            public readonly int Height;
            public readonly ColumnType Type;

            public ColInfoEx(int width, int height)
            {
                Contracts.CheckUserArg(width > 0, nameof(Column.ImageWidth));
                Contracts.CheckUserArg(height > 0, nameof(Column.ImageHeight));

                Width = width;
                Height = height;
                Type = new ImageType_Bitmap(Height, Width);
            }
        }

        internal const string Summary = "Scales an image to specified dimensions using one of the three scale types: isotropic with padding, "
            + "isotropic with cropping or anisotropic. In case of isotropic padding, transparent color is used to pad resulting image.";

        internal const string UserName = "Image Resizer Transform";
        public const string LoaderSignature = "ImageScalerTransform";
        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "IMGSCALF",
                //verWrittenCur: 0x00010001, // Initial
                verWrittenCur: 0x00010002, // Switch to Bitmap
                verReadableCur: 0x00010002,
                verWeCanReadBack: 0x00010002,
                loaderSignature: LoaderSignature);
        }

        private const string RegistrationName = "ImageScaler";

        public IResizeImageFunction Resize { get; }

        // This is parallel to Infos.
        private readonly ColInfoEx[] _exes;

        /// <summary>
        /// Public constructor corresponding to SignatureDataTransform.
        /// </summary>
        public ImageResizer_BitmapTransform(IHostEnvironment env, Arguments args, IDataView input)
            : base(env, RegistrationName, env.CheckRef(args, nameof(args)).Column, input, t => t is ImageType_Bitmap ? null : "Expected Image type")
        {
            Host.AssertNonEmpty(Infos);
            Host.Assert(Infos.Length == Utils.Size(args.Column));

            Resize = args.ResizeFunction.CreateComponent(env);
            _exes = new ColInfoEx[Infos.Length];
            for (int i = 0; i < _exes.Length; i++)
            {
                var item = args.Column[i];
                _exes[i] = new ColInfoEx(
                    item.ImageWidth ?? args.ImageWidth,
                    item.ImageHeight ?? args.ImageHeight);
            }
            Metadata.Seal();
        }

        private ImageResizer_BitmapTransform(IHost host, ModelLoadContext ctx, IDataView input)
            : base(host, ctx, input, t => t is ImageType_Bitmap ? null : "Expected Image type")
        {
            Host.AssertValue(ctx);

            // *** Binary format ***
            // <prefix handled in static Create method>
            // <base>
            // for each added column
            //   int: width
            //   int: height
            //   byte: scaling kind
            Host.AssertNonEmpty(Infos);

            _exes = new ColInfoEx[Infos.Length];
            for (int i = 0; i < _exes.Length; i++)
            {
                int width = ctx.Reader.ReadInt32();
                Host.CheckDecode(width > 0);
                int height = ctx.Reader.ReadInt32();
                Host.CheckDecode(height > 0);
                _exes[i] = new ColInfoEx(width, height);
            }
            Metadata.Seal();
        }

        public static ImageResizer_BitmapTransform Create(IHostEnvironment env, ModelLoadContext ctx, IDataView input)
        {
            Contracts.CheckValue(env, nameof(env));
            var h = env.Register(RegistrationName);
            h.CheckValue(ctx, nameof(ctx));
            h.CheckValue(input, nameof(input));
            ctx.CheckAtModel(GetVersionInfo());
            return h.Apply("Loading Model",
                ch =>
                {
                    // *** Binary format ***
                    // int: sizeof(Float)
                    // <remainder handled in ctors>
                    int cbFloat = ctx.Reader.ReadInt32();
                    ch.CheckDecode(cbFloat == sizeof(Single));
                    return new ImageResizer_BitmapTransform(h, ctx, input);
                });
        }

        public override void Save(ModelSaveContext ctx)
        {
            Host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel();
            ctx.SetVersionInfo(GetVersionInfo());

            // *** Binary format ***
            // int: sizeof(Float)
            // <base>
            // for each added column
            //   int: width
            //   int: height
            //   byte: scaling kind
            ctx.Writer.Write(sizeof(Single));
            SaveBase(ctx);

            Host.Assert(_exes.Length == Infos.Length);
            for (int i = 0; i < _exes.Length; i++)
            {
                var ex = _exes[i];
                ctx.Writer.Write(ex.Width);
                ctx.Writer.Write(ex.Height);
            }
        }

        protected override ColumnType GetColumnTypeCore(int iinfo)
        {
            Host.Check(0 <= iinfo && iinfo < Infos.Length);
            return _exes[iinfo].Type;
        }

        protected override Delegate GetGetterCore(IChannel ch, IRow input, int iinfo, out Action disposer)
        {
            Host.AssertValue(ch, "ch");
            Host.AssertValue(input);
            Host.Assert(0 <= iinfo && iinfo < Infos.Length);

            var src = default(Bitmap);
            var getSrc = GetSrcGetter<Bitmap>(input, iinfo);
            var ex = _exes[iinfo];
            
            disposer =
                () =>
                {
                    if (src != null)
                    {
                        src.Dispose();
                        src = null;
                    }
                };

            ValueGetter<Bitmap> del =
                (ref Bitmap dst) =>
                {
                    if (dst != null)
                        dst.Dispose();

                    getSrc(ref src);
                    if (src == null || src.Height <= 0 || src.Width <= 0)
                        return;
                    dst = Resize.Apply(src, ex.Width, ex.Height);
                    dst.Save("D:\\image.jpg");
                    Host.Assert(dst.Width == ex.Width && dst.Height == ex.Height);
                };

            return del;
        }
    }
}
