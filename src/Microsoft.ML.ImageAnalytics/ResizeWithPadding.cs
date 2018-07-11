using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.ImageAnalytics;
using Microsoft.ML.Runtime.Model;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

[assembly: LoadableClass(typeof(ResizeWithPadding), typeof(ResizeWithPadding.Arguments), typeof(SignatureResizer),
    ResizeWithPadding.UserName, ResizeWithPadding.LoadName)]

[assembly: LoadableClass(typeof(ResizeWithPadding), null, typeof(SignatureLoadModel),
     ResizeWithPadding.UserName, ResizeWithPadding.LoaderSignature)]
namespace Microsoft.ML.Runtime.ImageAnalytics
{
    public class ResizeWithPadding : ResizeImageFunctionBase
    {
        public const string UserName = "Resize with Padding";
        public const string LoadName = "ResizeWithPadding";
        public const string LoaderSignature = "ResizeWithPadding";

        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "IMGREPAD",
                verWrittenCur: 0x00010001,
                verReadableCur: 0x00010001,
                verWeCanReadBack: 0x00010001,
                loaderSignature: LoaderSignature);
        }

        [TlcModule.Component(Name = LoadName, FriendlyName = UserName)]
        public class Arguments : ArgumentsBase, ISupportResizeImageFunctionCombinerFactory
        {
            public IResizeImageFunction CreateComponent(IHostEnvironment env) => new ResizeWithPadding(env, this);
        }

        public ResizeWithPadding(IHostEnvironment env, Arguments args) : base(env, args, LoaderSignature)
        {
        }
        private ResizeWithPadding(IHostEnvironment env, ModelLoadContext ctx) : base(env, LoaderSignature, ctx)
        {
        }

        protected override void SaveCore(ModelSaveContext ctx)
        {
            base.SaveCore(ctx);
            ctx.SetVersionInfo(GetVersionInfo());
        }

        public override Bitmap Apply(Bitmap source, int width, int height)
        {
            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int destX = 0;
            int destY = 0;

            float aspect = 0;
            float widthAspect = 0;
            float heightAspect = 0;

            widthAspect = (float)width / sourceWidth;
            heightAspect = (float)height / sourceHeight;
            if (heightAspect < widthAspect)
            {
                aspect = heightAspect;
                destX = (int)((width - (sourceWidth * aspect)) / 2);
            }
            else
            {
                aspect = widthAspect;
                destY = (int)((height - (sourceHeight * aspect)) / 2);
            }

            int destWidth = (int)(sourceWidth * aspect);
            int destHeight = (int)(sourceHeight * aspect);


            return CreateBitmap(source, width, height, new Rectangle(destX, destY, destWidth, destHeight), new Rectangle(0, 0, sourceWidth, sourceHeight));
        }
    }
}
