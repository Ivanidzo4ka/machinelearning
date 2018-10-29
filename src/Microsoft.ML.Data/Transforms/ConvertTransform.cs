// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma warning disable 420 // volatile with Interlocked.CompareExchange

using Microsoft.ML.Core.Data;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Data.Conversion;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.Internal.Utilities;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.Runtime.Model.Onnx;
using Microsoft.ML.StaticPipe;
using Microsoft.ML.StaticPipe.Runtime;
using Microsoft.ML.Transforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[assembly: LoadableClass(ConvertTransform.Summary, typeof(IDataTransform), typeof(ConvertTransform), typeof(ConvertTransform.Arguments), typeof(SignatureDataTransform),
    ConvertTransform.UserName, ConvertTransform.ShortName, "ConvertTransform", DocName = "transform/ConvertTransform.md")]

[assembly: LoadableClass(ConvertTransform.Summary, typeof(IDataTransform), typeof(ConvertTransform), null, typeof(SignatureLoadDataTransform),
    ConvertTransform.UserName, ConvertTransform.LoaderSignature, ConvertTransform.LoaderSignatureOld)]

[assembly: LoadableClass(ConvertTransform.Summary, typeof(ConvertTransform), null, typeof(SignatureLoadModel),
    ConvertTransform.UserName, ConvertTransform.LoaderSignature)]

[assembly: LoadableClass(ConvertTransform.Summary, typeof(IRowMapper), typeof(ConvertTransform), null, typeof(SignatureLoadRowMapper),
    ConvertTransform.UserName, ConvertTransform.LoaderSignature)]

[assembly: EntryPointModule(typeof(TypeConversion))]

namespace Microsoft.ML.Transforms
{
    public static class TypeConversion
    {
        [TlcModule.EntryPoint(Name = "Transforms.ColumnTypeConverter", Desc = ConvertTransform.Summary, UserName = ConvertTransform.UserName, ShortName = ConvertTransform.ShortName)]
        public static CommonOutputs.TransformOutput Convert(IHostEnvironment env, ConvertTransform.Arguments input)
        {
            Contracts.CheckValue(env, nameof(env));
            env.CheckValue(input, nameof(input));

            var h = EntryPointUtils.CheckArgsAndCreateHost(env, "Convert", input);
            var view = ConvertTransform.Create(h, input, input.Data);
            return new CommonOutputs.TransformOutput()
            {
                Model = new TransformModel(h, view, input.Data),
                OutputData = view
            };
        }
    }

    public sealed class ConvertTransform : OneToOneTransformerBase
    {
        public class Column : OneToOneColumn
        {
            [Argument(ArgumentType.AtMostOnce, HelpText = "The result type", ShortName = "type")]
            public DataKind? ResultType;

            [Argument(ArgumentType.Multiple, HelpText = "For a key column, this defines the range of values", ShortName = "key", Visibility = ArgumentAttribute.VisibilityType.CmdLineOnly)]
            public KeyRange KeyRange;

            [Argument(ArgumentType.AtMostOnce, HelpText = "For a key column, this defines the range of values", ShortName = "key", Visibility = ArgumentAttribute.VisibilityType.EntryPointsOnly)]
            public string Range;

            public static Column Parse(string str)
            {
                var res = new Column();
                if (res.TryParse(str))
                    return res;
                return null;
            }

            protected override bool TryParse(string str)
            {
                Contracts.AssertNonEmpty(str);

                // We accept N:T:S where N is the new column name, T is the new type,
                // and S is source column names.
                string extra;
                if (!base.TryParse(str, out extra))
                    return false;
                if (extra == null)
                    return true;

                DataKind kind;
                if (!TypeParsingUtils.TryParseDataKind(extra, out kind, out KeyRange))
                    return false;
                ResultType = kind == default(DataKind) ? default(DataKind?) : kind;
                return true;
            }

            public bool TryUnparse(StringBuilder sb)
            {
                Contracts.AssertValue(sb);
                if (ResultType == null && KeyRange == null)
                    return TryUnparseCore(sb);

                if (!TrySanitize())
                    return false;
                if (CmdQuoter.NeedsQuoting(Name) || CmdQuoter.NeedsQuoting(Source))
                    return false;

                int ich = sb.Length;
                sb.Append(Name);
                sb.Append(':');
                if (ResultType != null)
                    sb.Append(ResultType.Value.GetString());
                if (KeyRange != null)
                {
                    sb.Append('[');
                    if (!KeyRange.TryUnparse(sb))
                    {
                        sb.Length = ich;
                        return false;
                    }
                    sb.Append(']');
                }
                else if (!string.IsNullOrEmpty(Range))
                    sb.Append(Range);
                sb.Append(':');
                sb.Append(Source);
                return true;
            }
        }

        public class Arguments : TransformInputBase
        {
            [Argument(ArgumentType.Multiple | ArgumentType.Required, HelpText = "New column definition(s) (optional form: name:type:src)", ShortName = "col", SortOrder = 1)]
            public Column[] Column;

            [Argument(ArgumentType.AtMostOnce, HelpText = "The result type", ShortName = "type", SortOrder = 2)]
            public DataKind? ResultType;

            [Argument(ArgumentType.Multiple, HelpText = "For a key column, this defines the range of values", ShortName = "key", Visibility = ArgumentAttribute.VisibilityType.CmdLineOnly)]
            public KeyRange KeyRange;

            // REVIEW: Consider supporting KeyRange type in entrypoints. This may require moving the KeyRange class to MLCore.
            [Argument(ArgumentType.AtMostOnce, HelpText = "For a key column, this defines the range of values", ShortName = "key", Visibility = ArgumentAttribute.VisibilityType.EntryPointsOnly)]
            public string Range;
        }

        internal const string Summary = "Converts a column to a different type, using standard conversions.";
        internal const string UserName = "Convert Transform";
        internal const string ShortName = "Convert";

        internal const string LoaderSignature = "ConvertTransform";
        internal const string LoaderSignatureOld = "ConvertFunction";
        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "CONVERTF",
                // verWrittenCur: 0x00010001, // Initial
                //verWrittenCur: 0x00010002, // Added support for keyRange
                verWrittenCur: 0x00010003, // Change to transformer leads to change of saving objects.
                verReadableCur: 0x00010003,
                verWeCanReadBack: 0x00010003,
                loaderSignature: LoaderSignature,
                loaderSignatureAlt: LoaderSignatureOld,
                loaderAssemblyName: typeof(ConvertTransform).Assembly.FullName);
        }

        private const string RegistrationName = "Convert";

        public IReadOnlyCollection<ColumnInfo> Columns => _columns.AsReadOnly();
        /// <summary>
        /// Describes how the transformer handles one column pair.
        /// </summary>
        public sealed class ColumnInfo
        {
            public readonly string Input;
            public readonly string Output;
            public readonly DataKind Kind;
            public readonly KeyRange KeyRange;

            public ColumnInfo(string input, string output, DataKind kind, KeyRange keyRange = null)
            {
                Input = input;
                Output = output;
                Kind = kind;
                KeyRange = keyRange;
            }
        }

        private readonly ColumnInfo[] _columns;

        private static (string input, string output)[] GetColumnPairs(ColumnInfo[] columns)
        {
            Contracts.CheckNonEmpty(columns, nameof(columns));
            return columns.Select(x => (x.Input, x.Output)).ToArray();
        }

        /// <summary>
        /// Create a <see cref="ConvertTransform"/> that takes multiple pairs of columns.
        /// </summary>
        public ConvertTransform(IHostEnvironment env, params ColumnInfo[] columns)
            : base(Contracts.CheckRef(env, nameof(env)).Register(nameof(KeyToValueTransform)), GetColumnPairs(columns))
        {
            _columns = columns.ToArray();
        }

        public override void Save(ModelSaveContext ctx)
        {
            Host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel();
            ctx.SetVersionInfo(GetVersionInfo());

            // *** Binary format ***
            // <base>
            // for each added column
            //   byte: data kind, with high bit set if there is a key range
            //   if there is a key range
            //     ulong: min
            //     ulong: max (0 for unspecified)
            //     byte: contiguous
            SaveColumns(ctx);

            for (int i = 0; i < _columns.Length; i++)
            {
                Host.Assert((DataKind)(byte)_columns[i].Kind == _columns[i].Kind);
                if (_columns[i].KeyRange != null)
                {
                    byte b = (byte)_columns[i].Kind;
                    b |= 0x80;
                    ctx.Writer.Write(b);
                    ctx.Writer.Write(_columns[i].KeyRange.Min);
                    ctx.Writer.Write(_columns[i].KeyRange.Max ?? 0);
                    ctx.Writer.WriteBoolByte(_columns[i].KeyRange.Contiguous);
                }
                else
                    ctx.Writer.Write((byte)_columns[i].Kind);
            }
        }

        // Factory method for SignatureLoadModel.
        private static ConvertTransform Create(IHostEnvironment env, ModelLoadContext ctx)
        {
            Contracts.CheckValue(env, nameof(env));
            var host = env.Register(RegistrationName);
            host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel(GetVersionInfo());
            return new ConvertTransform(host, ctx);
        }

        private ConvertTransform(IHost host, ModelLoadContext ctx)
        : base(host, ctx)
        {
            var columnsLength = ColumnPairs.Length;
            // *** Binary format ***
            // <base>
            // for each added column
            //   byte: data kind, with high bit set if there is a key range
            //   if there is a key range
            //     ulong: min
            //     ulong: max (0 for unspecified)
            //     byte: contiguous

            _columns = new ColumnInfo[columnsLength];
            for (int i = 0; i < columnsLength; i++)
            {
                byte b = ctx.Reader.ReadByte();
                var kind = (DataKind)(b & 0x7F);
                Host.CheckDecode(Enum.IsDefined(typeof(DataKind), kind));
                KeyRange range = null;
                if ((b & 0x80) != 0)
                {
                    range = new KeyRange();
                    range.Min = ctx.Reader.ReadUInt64();
                    ulong count = ctx.Reader.ReadUInt64();
                    if (count != 0)
                        range.Max = count;
                    range.Contiguous = ctx.Reader.ReadBoolByte();
                }
                _columns[i] = new ColumnInfo(ColumnPairs[i].input, ColumnPairs[i].output, kind, range);
            }
        }

        // Factory method for SignatureDataTransform.
        internal static IDataTransform Create(IHostEnvironment env, Arguments args, IDataView input)
        {
            Contracts.CheckValue(env, nameof(env));
            env.CheckValue(args, nameof(args));
            env.CheckValue(input, nameof(input));

            env.CheckValue(args.Column, nameof(args.Column));
            var cols = new ColumnInfo[args.Column.Length];
            for (int i = 0; i < cols.Length; i++)
            {
                var item = args.Column[i];
                var tempResultType = item.ResultType ?? args.ResultType;
                DataKind kind;
                KeyRange range = item.KeyRange ?? (item.Range != null ? KeyRange.Parse(item.Range) : null) ?? args.KeyRange ?? (args.Range != null ? KeyRange.Parse(args.Range) : null);
                if (tempResultType == null)
                {
                    if (range == null)
                        kind = DataKind.Num;
                    else
                    {
                        var srcType = input.Schema[item.Source ?? item.Name].Type;
                        kind = srcType.IsKey ? srcType.RawKind : DataKind.U4;
                    }
                }
                else
                {
                    kind = tempResultType.Value;
                }
                cols[i] = new ColumnInfo(item.Source ?? item.Name, item.Name, kind, range);
            };
            return new ConvertTransform(env, cols).MakeDataTransform(input);
        }

        // Factory method for SignatureLoadDataTransform.
        private static IDataTransform Create(IHostEnvironment env, ModelLoadContext ctx, IDataView input)
            => Create(env, ctx).MakeDataTransform(input);

        // Factory method for SignatureLoadRowMapper.
        private static IRowMapper Create(IHostEnvironment env, ModelLoadContext ctx, ISchema inputSchema)
            => Create(env, ctx).MakeRowMapper(inputSchema);

        protected override IRowMapper MakeRowMapper(ISchema schema) => new Mapper(this, Schema.Create(schema));

        internal static bool GetNewType(IExceptionContext ectx, ColumnType srcType, DataKind kind, KeyRange range, out PrimitiveType itemType)
        {
            if (range != null)
            {
                itemType = TypeParsingUtils.ConstructKeyType(kind, range);
                if (!srcType.ItemType.IsKey && !srcType.ItemType.IsText)
                    return false;
            }
            else if (!srcType.ItemType.IsKey)
                itemType = PrimitiveType.FromKind(kind);
            else if (!KeyType.IsValidDataKind(kind))
            {
                itemType = PrimitiveType.FromKind(kind);
                return false;
            }
            else
            {
                var key = srcType.ItemType.AsKey;
                ectx.Assert(KeyType.IsValidDataKind(key.RawKind));
                int count = key.Count;
                // Technically, it's an error for the counts not to match, but we'll let the Conversions
                // code return false below. There's a possibility we'll change the standard conversions to
                // map out of bounds values to zero, in which case, this is the right thing to do.
                ulong max = kind.ToMaxInt();
                if ((ulong)count > max)
                    count = (int)max;
                itemType = new KeyType(kind, key.Min, count, key.Contiguous);
            }
            return true;
        }

        private sealed class Mapper : MapperBase, ICanSaveOnnx
        {
            private readonly ConvertTransform _parent;
            private readonly ColumnType[] _types;
            private readonly int[] _srcCols;

            public bool CanSaveOnnx(OnnxContext ctx) => ctx.GetOnnxVersion() == OnnxVersion.Experimental;

            public Mapper(ConvertTransform parent, Schema inputSchema)
                : base(parent.Host.Register(nameof(Mapper)), parent, inputSchema)
            {
                _parent = parent;
                _types = new ColumnType[_parent._columns.Length];
                _srcCols = new int[_parent._columns.Length];
                for (int i = 0; i < _parent._columns.Length; i++)
                {
                    inputSchema.TryGetColumnIndex(_parent.ColumnPairs[i].input, out _srcCols[i]);
                    var srcCol = inputSchema[_srcCols[i]];
                    if (!CanConvertToType(Host, srcCol.Type, _parent._columns[i].Kind, _parent._columns[i].KeyRange, out PrimitiveType itemType, out _types[i]))
                    {
                        throw Host.ExceptParam(nameof(inputSchema),
                        "source column '{0}' with item type '{1}' is not compatible with destination type '{2}'",
                        _parent._columns[i].Input, srcCol.Type, itemType);
                    }
                }
            }
            private static bool CanConvertToType(IExceptionContext ectx, ColumnType srcType, DataKind kind, KeyRange range, out PrimitiveType itemType, out ColumnType typeDst)
            {
                ectx.AssertValue(srcType);
                ectx.Assert(Enum.IsDefined(typeof(DataKind), kind));

                typeDst = null;
                if (!GetNewType(ectx, srcType, kind, range, out itemType))
                    return false;

                // Ensure that the conversion is legal. We don't actually cache the delegate here. It will get
                // re-fetched by the utils code when needed.
                if (!Conversions.Instance.TryGetStandardConversion(srcType.ItemType, itemType, out Delegate del, out bool identity))
                    return false;

                typeDst = itemType;
                if (srcType.IsVector)
                    typeDst = new VectorType(itemType, srcType.AsVector);

                return true;
            }

            public override Schema.Column[] GetOutputColumns()
            {
                var result = new Schema.Column[_parent._columns.Length];
                for (int i = 0; i < _parent._columns.Length; i++)
                {
                    var builder = new Schema.Metadata.Builder();
                    var srcType = InputSchema[_srcCols[i]].Type;
                    if (_types[i].IsKnownSizeVector)
                        builder.Add(InputSchema[ColMapNewToOld[i]].Metadata, name => name == MetadataUtils.Kinds.SlotNames);
                    if (srcType.ItemType.IsKey && _types[i].ItemType.IsKey &&
                        srcType.ItemType.KeyCount > 0 &&
                        srcType.ItemType.KeyCount == _types[i].ItemType.KeyCount)
                        builder.Add(InputSchema[ColMapNewToOld[i]].Metadata, name => name == MetadataUtils.Kinds.KeyValues);
                    if (srcType.ItemType.IsNumber && _types[i].ItemType.IsNumber)
                        builder.Add(InputSchema[ColMapNewToOld[i]].Metadata, name => name == MetadataUtils.Kinds.IsNormalized);
                    if (srcType.IsBool && _types[i].ItemType.IsNumber)
                    {
                        ValueGetter<bool> getter = (ref bool dst) =>
                        {
                            dst = true;
                        };
                        builder.Add(new Schema.Column(MetadataUtils.Kinds.IsNormalized, BoolType.Instance, null), getter);
                    }
                    result[i] = new Schema.Column(_parent._columns[i].Output, _types[i], builder.GetMetadata());
                }
                return result;
            }

            protected override Delegate MakeGetter(IRow input, int iinfo, out Action disposer)
            {
                Contracts.AssertValue(input);
                Contracts.Assert(0 <= iinfo && iinfo < _parent.ColumnPairs.Length);
                disposer = null;
                if (!_types[iinfo].IsVector)
                    return RowCursorUtils.GetGetterAs(_types[iinfo], input, _srcCols[iinfo]);
                return RowCursorUtils.GetVecGetterAs(_types[iinfo].AsVector.ItemType, input, _srcCols[iinfo]);
            }

            public void SaveAsOnnx(OnnxContext ctx)
            {
                Host.CheckValue(ctx, nameof(ctx));

                for (int iinfo = 0; iinfo < _parent._columns.Length; ++iinfo)
                {
                    string sourceColumnName = _parent._columns[iinfo].Input;
                    if (!ctx.ContainsColumn(sourceColumnName))
                    {
                        ctx.RemoveColumn(_parent._columns[iinfo].Output, false);
                        continue;
                    }

                    if (!SaveAsOnnxCore(ctx, iinfo, ctx.GetVariableName(sourceColumnName),
                        ctx.AddIntermediateVariable(_types[iinfo], _parent._columns[iinfo].Output)))
                    {
                        ctx.RemoveColumn(_parent._columns[iinfo].Output, true);
                    }
                }
            }

            private bool SaveAsOnnxCore(OnnxContext ctx, int iinfo, string srcVariableName, string dstVariableName)
            {
                var opType = "CSharp";
                var node = ctx.CreateNode(opType, srcVariableName, dstVariableName, ctx.GetNodeName(opType));
                node.AddAttribute("type", LoaderSignature);
                node.AddAttribute("to", (byte)_parent._columns[iinfo].Kind);
                if (_parent._columns[iinfo].KeyRange != null)
                {
                    var key = _types[iinfo].ItemType.AsKey;
                    node.AddAttribute("min", key.Min);
                    node.AddAttribute("max", key.Count);
                    node.AddAttribute("contiguous", key.Contiguous);
                }
                return true;
            }
        }
    }

    public sealed class ConvertEstimator : TrivialEstimator<ConvertTransform>
    {
        /// <summary>
        /// Convinence constructor for simple one column case
        /// </summary>
        /// <param name="env">Host Environment.</param>
        /// <param name="inputColumn">Name of the output column.</param>
        /// <param name="outputColumn">Name of the column to be transformed. If this is null '<paramref name="inputColumn"/>' will be used.</param>
        /// <param name="resultType">The expected type of the converted column.</param>
        public ConvertEstimator(IHostEnvironment env,
            string inputColumn, string outputColumn = null,
            DataKind resultType = DataKind.R4)
            : this(env, new ConvertTransform.ColumnInfo(inputColumn, outputColumn ?? inputColumn, resultType))
        {
        }

        public ConvertEstimator(IHostEnvironment env, params ConvertTransform.ColumnInfo[] columns) :
            base(Contracts.CheckRef(env, nameof(env)).Register(nameof(ConvertEstimator)), new ConvertTransform(env, columns))
        {
        }

        public override SchemaShape GetOutputSchema(SchemaShape inputSchema)
        {
            Host.CheckValue(inputSchema, nameof(inputSchema));
            var result = inputSchema.Columns.ToDictionary(x => x.Name);
            foreach (var colInfo in Transformer.Columns)
            {
                if (!inputSchema.TryFindColumn(colInfo.Input, out var col))
                    throw Host.ExceptSchemaMismatch(nameof(inputSchema), "input", colInfo.Input);
                if (!ConvertTransform.GetNewType(Host, col.ItemType, colInfo.Kind, colInfo.KeyRange, out PrimitiveType newType))
                    throw Host.ExceptParam(nameof(inputSchema), $"Can't convert {colInfo.Input} into {newType.ToString()}");
                if (!Conversions.Instance.TryGetStandardConversion(col.ItemType, newType, out Delegate del, out bool identity))
                    throw Host.ExceptParam(nameof(inputSchema), $"Don't know how to convert {colInfo.Input} into {newType.ToString()}");
                var metadata = new List<SchemaShape.Column>();
                if (col.ItemType.IsBool && newType.ItemType.IsNumber)
                    metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.IsNormalized, SchemaShape.Column.VectorKind.Scalar, BoolType.Instance, false));
                if (col.Metadata.TryFindColumn(MetadataUtils.Kinds.SlotNames, out var slotMeta))
                    if (col.Kind == SchemaShape.Column.VectorKind.Vector)
                        metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.SlotNames, SchemaShape.Column.VectorKind.Vector, slotMeta.ItemType, false));
                if (col.Metadata.TryFindColumn(MetadataUtils.Kinds.KeyValues, out var keyMeta))
                    if (col.ItemType.IsKey)
                        metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.KeyValues, SchemaShape.Column.VectorKind.Vector, keyMeta.ItemType, false));
                if (col.Metadata.TryFindColumn(MetadataUtils.Kinds.IsNormalized, out var normMeta))
                    if (col.ItemType.IsNumber && newType.ItemType.IsNumber)
                        metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.KeyValues, SchemaShape.Column.VectorKind.Vector, normMeta.ItemType, false));
                result[colInfo.Output] = new SchemaShape.Column(colInfo.Output, col.Kind, newType, false, col.Metadata);
            }
            return new SchemaShape(result.Values);
        }
    }
    public static partial class ConvertStaticExtensions
    {

        private interface IConvertCol
        {
            PipelineColumn Input { get; }
            DataKind Kind { get; }
        }

        private sealed class ImplScalar<T> : Scalar<float>, IConvertCol
        {
            public PipelineColumn Input { get; }
            public DataKind Kind { get; }
            public ImplScalar(PipelineColumn input, DataKind kind) : base(Rec.Inst, input)
            {
                Input = input;
                Kind = kind;
            }
        }

        private sealed class ImplVector<T> : Vector<float>, IConvertCol
        {
            public PipelineColumn Input { get; }
            public DataKind Kind { get; }
            public ImplVector(PipelineColumn input, DataKind kind) : base(Rec.Inst, input)
            {
                Input = input;
                Kind = kind;
            }
        }

        private sealed class ImplVarVector<T> : VarVector<float>, IConvertCol
        {
            public PipelineColumn Input { get; }
            public DataKind Kind { get; }
            public ImplVarVector(PipelineColumn input, DataKind kind) : base(Rec.Inst, input)
            {
                Input = input;
                Kind = kind;
            }
        }

        private sealed class Rec : EstimatorReconciler
        {
            public static readonly Rec Inst = new Rec();

            public override IEstimator<ITransformer> Reconcile(IHostEnvironment env, PipelineColumn[] toOutput,
                IReadOnlyDictionary<PipelineColumn, string> inputNames, IReadOnlyDictionary<PipelineColumn, string> outputNames, IReadOnlyCollection<string> usedNames)
            {
                var infos = new ConvertTransform.ColumnInfo[toOutput.Length];
                for (int i = 0; i < toOutput.Length; ++i)
                {
                    var tcol = (IConvertCol)toOutput[i];
                    infos[i] = new ConvertTransform.ColumnInfo(inputNames[tcol.Input], outputNames[toOutput[i]], tcol.Kind);
                }
                return new ConvertEstimator(env, infos);
            }
        }
    }
}
