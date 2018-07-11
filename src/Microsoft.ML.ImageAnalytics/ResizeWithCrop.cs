using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.ImageAnalytics;
using Microsoft.ML.Runtime.Model;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

[assembly: LoadableClass(typeof(ResizeWithCrop), typeof(ResizeWithCrop.Arguments), typeof(SignatureResizer),
    ResizeWithCrop.UserName, ResizeWithCrop.LoadName)]

[assembly: LoadableClass(typeof(ResizeWithCrop), null, typeof(SignatureLoadModel),
     ResizeWithCrop.UserName, ResizeWithCrop.LoaderSignature)]
namespace Microsoft.ML.Runtime.ImageAnalytics
{
    public class ResizeWithCrop : ResizeImageFunctionBase
    {
        public const string UserName = "Resize with Cropping";
        public const string LoadName = "ResizeWithCropping";
        public const string LoaderSignature = "ResizeWithCropping";

        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "IMGRECRP",
                verWrittenCur: 0x00010001,
                verReadableCur: 0x00010001,
                verWeCanReadBack: 0x00010001,
                loaderSignature: LoaderSignature);
        }

        public enum Anchor
        {
            Right = 0,
            Left = 1,
            Top = 2,
            Bottom = 3,
            Center = 4
        }

        [TlcModule.Component(Name = LoadName, FriendlyName = UserName)]
        public class Arguments : ArgumentsBase, ISupportResizeImageFunctionCombinerFactory
        {
            public Anchor Anchor = Anchor.Center;

            public IResizeImageFunction CreateComponent(IHostEnvironment env) => new ResizeWithCrop(env, this);
        }

        private Anchor _anchor;
        public ResizeWithCrop(IHostEnvironment env, Arguments args) : base(env, args, LoaderSignature)
        {
            _anchor = args.Anchor;
        }

        private ResizeWithCrop(IHostEnvironment env, ModelLoadContext ctx) : base(env, LoaderSignature, ctx)
        {
            _anchor = (Anchor)ctx.Reader.ReadInt32();
            Host.CheckDecode(Enum.IsDefined(typeof(Anchor), _anchor));
        }
        protected override void SaveCore(ModelSaveContext ctx)
        {
            base.SaveCore(ctx);
            ctx.SetVersionInfo(GetVersionInfo());
            // *** Binary format ***
            // int: _anchor
            Contracts.Assert(Enum.IsDefined(typeof(Anchor), _anchor));
            ctx.Writer.Write((int)_anchor);


        }
        public override Bitmap Apply(Bitmap source, int width, int height)
        {
            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int sourceX = 0;
            int sourceY = 0;
            int destX = 0;
            int destY = 0;

            float aspect = 0;
            float widthAspect = 0;
            float heightAspect = 0;

            widthAspect = (float)width / sourceWidth;
            heightAspect = (float)height / sourceHeight;

            if (heightAspect < widthAspect)
            {
                aspect = widthAspect;
                switch (_anchor)
                {
                    case Anchor.Top:
                        destY = 0;
                        break;
                    case Anchor.Bottom:
                        destY = (int)(height - (sourceHeight * aspect));
                        break;
                    default:
                        destY = (int)((height - (sourceHeight * aspect)) / 2);
                        break;
                }
            }
            else
            {
                aspect = heightAspect;
                switch (_anchor)
                {
                    case Anchor.Left:
                        destX = 0;
                        break;
                    case Anchor.Right:
                        destX = (int)(width - (sourceWidth * aspect));
                        break;
                    default:
                        destX = (int)((width - (sourceWidth * aspect)) / 2);
                        break;
                }
            }

            int destWidth = (int)(sourceWidth * aspect);
            int destHeight = (int)(sourceHeight * aspect);

            return CreateBitmap(source, width, height, new Rectangle(destX, destY, destWidth, destHeight), new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight));
        }
    }
}
