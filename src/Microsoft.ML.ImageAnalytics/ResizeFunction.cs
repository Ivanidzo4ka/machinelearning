using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.Internal.Internallearn;
using Microsoft.ML.Runtime.Model;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Microsoft.ML.Runtime.ImageAnalytics
{

    public delegate void SignatureResizer();
    [TlcModule.ComponentKind("ResizeImageFunction")]
    public interface ISupportResizeImageFunctionCombinerFactory : IComponentFactory<IResizeImageFunction>
    {

    }
    public interface IResizeImageFunction
    {
        Bitmap Apply(Bitmap source, int width, int height);
    }

    public abstract class ResizeImageFunctionBase : IResizeImageFunction
    {
        protected readonly IHost Host;

        protected SmoothingMode _smoothingMode;
        protected InterpolationMode _interpolationMode;
        protected PixelOffsetMode _pixelOffsetMode;

        public ResizeImageFunctionBase(IHostEnvironment env, ArgumentsBase args, string name)
        {
            Contracts.CheckValue(env, nameof(env));
            env.CheckNonWhiteSpace(name, nameof(name));
            Host = env.Register(name);
            Contracts.CheckValue(args, nameof(args));
            _smoothingMode = args.SmoothingMode;
            _interpolationMode = args.InterpolationMode;
            _pixelOffsetMode = args.PixelOffsetMode;
        }

        public void Save(ModelSaveContext ctx)
        {
            Host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel();
            SaveCore(ctx);
        }

        protected ResizeImageFunctionBase(IHostEnvironment env, string name, ModelLoadContext ctx)
        {
            Contracts.AssertValue(env);
            env.AssertNonWhiteSpace(name);
            Host = env.Register(name);
            Host.CheckValue(ctx, nameof(ctx));

            // *** Binary format ***
            // int: _smoothingMode
            // int: _interpolationMode
            // int: _pixelOffsetMode

            _smoothingMode = (SmoothingMode)ctx.Reader.ReadInt32();
            Host.CheckDecode(Enum.IsDefined(typeof(SmoothingMode), _smoothingMode));
            _interpolationMode = (InterpolationMode)ctx.Reader.ReadInt32();
            Host.CheckDecode(Enum.IsDefined(typeof(InterpolationMode), _interpolationMode));
            _pixelOffsetMode = (PixelOffsetMode)ctx.Reader.ReadInt32();
            Host.CheckDecode(Enum.IsDefined(typeof(PixelOffsetMode), _pixelOffsetMode));
        }
        protected virtual void SaveCore(ModelSaveContext ctx)
        {
            // *** Binary format ***
            // int: _smoothingMode
            // int: _interpolationMode
            // int: _pixelOffsetMode

            Contracts.Assert(Enum.IsDefined(typeof(SmoothingMode), _smoothingMode));
            ctx.Writer.Write((int)_smoothingMode);
            Contracts.Assert(Enum.IsDefined(typeof(InterpolationMode), _interpolationMode));
            ctx.Writer.Write((int)_interpolationMode);
            Contracts.Assert(Enum.IsDefined(typeof(PixelOffsetMode), _pixelOffsetMode));
            ctx.Writer.Write((int)_pixelOffsetMode);
        }


        public abstract class ArgumentsBase
        {
            [Argument(ArgumentType.AtMostOnce, HelpText = "The smoothing mode specifies whether lines, curves, and the edges of filled areas use smoothing (also called antialiasing).", ShortName = "smooth", SortOrder = 1)]
            [TGUI(Label = "Smoothing mode", Description = "The smoothing mode specifies whether lines, curves, and the edges of filled areas use smoothing (also called antialiasing).")]
            public SmoothingMode SmoothingMode;

            [Argument(ArgumentType.AtMostOnce, HelpText = "The interpolation mode determines how intermediate values between two endpoints are calculated.", ShortName = "inter", SortOrder = 2)]
            [TGUI(Label = "Interpolation mode", Description = "The interpolation mode determines how intermediate values between two endpoints are calculated.")]
            public InterpolationMode InterpolationMode;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Specifies how pixels are offset during rendering.", ShortName = "poffset", SortOrder = 3)]
            [TGUI(Label = "Pixel offset mode", Description = "Specifies how pixels are offset during rendering.")]
            public PixelOffsetMode PixelOffsetMode;
        }

        public abstract Bitmap Apply(Bitmap source, int width, int height);

        protected Bitmap CreateBitmap(Bitmap source, int width, int height, Rectangle destRectangle, Rectangle sourceRectangle)
        {
            var bmPhoto = new Bitmap(width, height);
            bmPhoto.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using (var g = Graphics.FromImage(bmPhoto))
            {
                g.InterpolationMode = _interpolationMode;
                g.SmoothingMode = _smoothingMode;
                g.PixelOffsetMode = _pixelOffsetMode;
                g.DrawImage(source, destRectangle, sourceRectangle, GraphicsUnit.Pixel);
            }

            return bmPhoto;
        }
    }
}
