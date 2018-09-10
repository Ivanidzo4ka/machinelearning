// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.ML.Core.Data;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Internal.Utilities;
using Microsoft.ML.Runtime.Model;

[assembly: LoadableClass(KeyToBinaryVectorTransform.Summary, typeof(IDataTransform), typeof(KeyToBinaryVectorTransform), typeof(KeyToBinaryVectorTransform.Arguments), typeof(SignatureDataTransform),
    "Key To Binary Vector Transform", KeyToBinaryVectorTransform.UserName, "KeyToBinary", "ToVector", DocName = "transform/KeyToBinaryVectorTransform.md")]

[assembly: LoadableClass(KeyToBinaryVectorTransform.Summary, typeof(IDataView), typeof(KeyToBinaryVectorTransform), null, typeof(SignatureLoadDataTransform),
    "Key To Binary Vector Transform", KeyToBinaryVectorTransform.LoaderSignature)]

[assembly: LoadableClass(KeyToBinaryVectorTransform.Summary, typeof(KeyToBinaryVectorTransform), null, typeof(SignatureLoadModel),
    KeyToBinaryVectorTransform.UserName, KeyToBinaryVectorTransform.LoaderSignature)]

[assembly: LoadableClass(typeof(IRowMapper), typeof(KeyToBinaryVectorTransform), null, typeof(SignatureLoadRowMapper),
   KeyToBinaryVectorTransform.UserName, KeyToBinaryVectorTransform.LoaderSignature)]

namespace Microsoft.ML.Runtime.Data
{
    public sealed class KeyToBinaryVectorTransform : OneToOneTransformerBase
    {
        public sealed class Arguments
        {
            [Argument(ArgumentType.Multiple | ArgumentType.Required, HelpText = "New column definition(s) (optional form: name:src)",
                ShortName = "col", SortOrder = 1)]
            public KeyToVectorTransform.Column[] Column;
        }
        public class ColumnInfo
        {
            public readonly string Input;
            public readonly string Output;

            public ColumnInfo(string input, string output)
            {
                Input = input;
                Output = output;
            }
        }

        internal sealed class ColInfo
        {
            public readonly string Name;
            public readonly string Source;
            public readonly ColumnType TypeSrc;

            public ColInfo(string name, string source, ColumnType type)
            {
                Name = name;
                Source = source;
                TypeSrc = type;
            }
        }

        internal const string Summary = "Converts a key column to a binary encoded vector.";
        public const string UserName = "KeyToBinaryVectorTransform";
        public const string LoaderSignature = "KeyToBinaryTransform";
        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "KEY2BINR",
                verWrittenCur: 0x00000001, // Initial
                verReadableCur: 0x00000001,
                verWeCanReadBack: 0x00000001,
                loaderSignature: LoaderSignature);
        }

        private const string RegistrationName = "KeyToBinary";

        private static (string input, string output)[] GetColumnPairs(ColumnInfo[] columns)
        {
            Contracts.CheckValue(columns, nameof(columns));
            return columns.Select(x => (x.Input, x.Output)).ToArray();
        }

        private string TestIsKey(ColumnType type)
        {
            if (type.ItemType.KeyCount > 0)
                return null;
            return "key type of known cardinality";
        }

        private ColInfo[] CreateInfos(ISchema inputSchema)
        {
            Host.AssertValue(inputSchema);
            var infos = new ColInfo[ColumnPairs.Length];
            for (int i = 0; i < ColumnPairs.Length; i++)
            {
                if (!inputSchema.TryGetColumnIndex(ColumnPairs[i].input, out int colSrc))
                    throw Host.ExceptUserArg(nameof(ColumnPairs), "Source column '{0}' not found", ColumnPairs[i].input);
                var type = inputSchema.GetColumnType(colSrc);
                string reason = TestIsKey(type);
                if (reason != null)
                    throw Host.ExceptSchemaMismatch(nameof(inputSchema), "input", ColumnPairs[i].input, reason, type.ToString());
                infos[i] = new ColInfo(ColumnPairs[i].output, ColumnPairs[i].input, type);
            }
            return infos;
        }

        public KeyToBinaryVectorTransform(IHostEnvironment env, IDataView input, ColumnInfo[] columns)
            : base(Contracts.CheckRef(env, nameof(env)).Register(RegistrationName), GetColumnPairs(columns))
        {
            // Validate input schema.
            CreateInfos(input.Schema);
        }

        public override void Save(ModelSaveContext ctx)
        {
            Host.CheckValue(ctx, nameof(ctx));

            // *** Binary format ***
            // <prefix handled in static Create method>
            // <base>
            ctx.CheckAtModel();
            ctx.SetVersionInfo(GetVersionInfo());
            SaveColumns(ctx);
        }

        // Factory method for SignatureLoadModel.
        public static KeyToBinaryVectorTransform Create(IHostEnvironment env, ModelLoadContext ctx)
        {
            Contracts.CheckValue(env, nameof(env));
            var host = env.Register(RegistrationName);

            host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel(GetVersionInfo());

            return new KeyToBinaryVectorTransform(host, ctx);
        }

        private KeyToBinaryVectorTransform(IHost host, ModelLoadContext ctx)
            : base(host, ctx)
        {
        }

        public static IDataTransform Create(IHostEnvironment env, IDataView input, params ColumnInfo[] columns) =>
            new KeyToBinaryVectorTransform(env, input, columns).MakeDataTransform(input);

        // Factory method for SignatureDataTransform.
        public static IDataTransform Create(IHostEnvironment env, Arguments args, IDataView input)
        {
            Contracts.CheckValue(env, nameof(env));
            env.CheckValue(args, nameof(args));
            env.CheckValue(input, nameof(input));

            env.CheckValue(args.Column, nameof(args.Column));
            var cols = new ColumnInfo[args.Column.Length];
            using (var ch = env.Start("ValidateArgs"))
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    var item = args.Column[i];
                    cols[i] = new ColumnInfo(item.Source, item.Name);
                };
            }
            return new KeyToBinaryVectorTransform(env, input, cols).MakeDataTransform(input);
        }

        // Factory method for SignatureLoadDataTransform.
        public static IDataTransform Create(IHostEnvironment env, ModelLoadContext ctx, IDataView input)
            => Create(env, ctx).MakeDataTransform(input);

        // Factory method for SignatureLoadRowMapper.
        public static IRowMapper Create(IHostEnvironment env, ModelLoadContext ctx, ISchema inputSchema)
            => Create(env, ctx).MakeRowMapper(inputSchema);

        protected override IRowMapper MakeRowMapper(ISchema schema) => new Mapper(this, schema);

        private sealed class Mapper : MapperBase
        {
            private readonly KeyToBinaryVectorTransform _parent;
            private readonly ColInfo[] _infos;
            private readonly VectorType[] _types;
            private readonly int[] _bitsPerKey;

            public Mapper(KeyToBinaryVectorTransform parent, ISchema inputSchema)
                : base(parent.Host.Register(nameof(Mapper)), parent, inputSchema)
            {
                _parent = parent;
                _infos = _parent.CreateInfos(inputSchema);
                _types = new VectorType[_parent.ColumnPairs.Length];
                _bitsPerKey = new int[_parent.ColumnPairs.Length];
                for (int i = 0; i < _parent.ColumnPairs.Length; i++)
                {
                    //Add an additional bit for all 1s to represent missing values.
                    _bitsPerKey[i] = Utils.IbitHigh((uint)_infos[i].TypeSrc.ItemType.KeyCount) + 2;
                    Host.Assert(_bitsPerKey[i] > 0);
                    if (_infos[i].TypeSrc.ValueCount == 1)
                        // Output is a single vector computed as the sum of the output indicator vectors.
                        _types[i] = new VectorType(NumberType.Float, _bitsPerKey[i]);
                    else
                        // Output is the concatenation of the multiple output indicator vectors.
                        _types[i] = new VectorType(NumberType.Float, _infos[i].TypeSrc.ValueCount, _bitsPerKey[i]);
                }
            }

            public override RowMapperColumnInfo[] GetOutputColumns()
            {
                var result = new RowMapperColumnInfo[_parent.ColumnPairs.Length];
                for (int i = 0; i < _parent.ColumnPairs.Length; i++)
                {
                    InputSchema.TryGetColumnIndex(_parent.ColumnPairs[i].input, out int colIndex);
                    Host.Assert(colIndex >= 0);
                    var colMetaInfo = new ColumnMetadataInfo(_parent.ColumnPairs[i].output);
                    AddMetadata(i, colMetaInfo);

                    result[i] = new RowMapperColumnInfo(_parent.ColumnPairs[i].output, _types[i], colMetaInfo);
                }
                return result;
            }

            private void AddMetadata(int i, ColumnMetadataInfo colMetaInfo)
            {
                InputSchema.TryGetColumnIndex(_infos[i].Source, out int srcCol);
                var srcType = _infos[i].TypeSrc;
                // See if the source has key names.
                var typeNames = InputSchema.GetMetadataTypeOrNull(MetadataUtils.Kinds.KeyValues, srcCol);
                if (typeNames == null || !typeNames.IsKnownSizeVector || !typeNames.ItemType.IsText ||
                    typeNames.VectorSize != _infos[i].TypeSrc.ItemType.KeyCount)
                {
                    typeNames = null;
                }

                if (_infos[i].TypeSrc.ValueCount == 1)
                {
                    if (typeNames != null)
                    {
                        MetadataUtils.MetadataGetter<VBuffer<DvText>> getter = (int col, ref VBuffer<DvText> dst) =>
                        {
                            GenerateBitSlotName(i, ref dst);
                        };
                        var info = new MetadataInfo<VBuffer<DvText>>(new VectorType(TextType.Instance, _types[i]), getter);
                        colMetaInfo.Add(MetadataUtils.Kinds.SlotNames, info);
                    }
                    MetadataUtils.MetadataGetter<DvBool> normalizeGetter = (int col, ref DvBool dst) =>
                    {
                        dst = true;
                    };
                    var normalizeInfo = new MetadataInfo<DvBool>(BoolType.Instance, normalizeGetter);
                    colMetaInfo.Add(MetadataUtils.Kinds.IsNormalized, normalizeInfo);
                }
                else
                {
                    if (typeNames != null && _types[i].IsKnownSizeVector)
                    {
                        MetadataUtils.MetadataGetter<VBuffer<DvText>> getter = (int col, ref VBuffer<DvText> dst) =>
                        {
                            GetSlotNames(i, ref dst);
                        };
                        var info = new MetadataInfo<VBuffer<DvText>>(new VectorType(TextType.Instance, _types[i]), getter);
                        colMetaInfo.Add(MetadataUtils.Kinds.SlotNames, info);
                    }
                }
            }

            private void GenerateBitSlotName(int iinfo, ref VBuffer<DvText> dst)
            {
                const string slotNamePrefix = "Bit";
                var bldr = new BufferBuilder<DvText>(TextCombiner.Instance);
                bldr.Reset(_bitsPerKey[iinfo], true);
                for (int i = 0; i < _bitsPerKey[iinfo]; i++)
                    bldr.AddFeature(i, new DvText(slotNamePrefix + (_bitsPerKey[iinfo] - i - 1)));

                bldr.GetResult(ref dst);
            }

            private void GetSlotNames(int iinfo, ref VBuffer<DvText> dst)
            {
                Host.Assert(0 <= iinfo && iinfo < _infos.Length);
                Host.Assert(_types[iinfo].IsKnownSizeVector);

                // Variable size should have thrown (by the caller).
                var typeSrc = _infos[iinfo].TypeSrc;
                Host.Assert(typeSrc.VectorSize > 1);

                // Get the source slot names, defaulting to empty text.
                var namesSlotSrc = default(VBuffer<DvText>);
                InputSchema.TryGetColumnIndex(_infos[iinfo].Source, out int srcCol);
                Host.Assert(srcCol >= 0);
                var typeSlotSrc = InputSchema.GetMetadataTypeOrNull(MetadataUtils.Kinds.SlotNames, srcCol);
                if (typeSlotSrc != null && typeSlotSrc.VectorSize == typeSrc.VectorSize && typeSlotSrc.ItemType.IsText)
                {
                    InputSchema.GetMetadata(MetadataUtils.Kinds.SlotNames, srcCol, ref namesSlotSrc);
                    Host.Check(namesSlotSrc.Length == typeSrc.VectorSize);
                }
                else
                    namesSlotSrc = VBufferUtils.CreateEmpty<DvText>(typeSrc.VectorSize);

                int slotLim = _types[iinfo].VectorSize;
                Host.Assert(slotLim == (long)typeSrc.VectorSize * _bitsPerKey[iinfo]);

                var values = dst.Values;
                if (Utils.Size(values) < slotLim)
                    values = new DvText[slotLim];

                var sb = new StringBuilder();
                int slot = 0;
                VBuffer<DvText> bits = default;
                GenerateBitSlotName(iinfo, ref bits);
                foreach (var kvpSlot in namesSlotSrc.Items(all: true))
                {
                    Contracts.Assert(slot == (long)kvpSlot.Key * _bitsPerKey[iinfo]);
                    sb.Clear();
                    if (kvpSlot.Value.HasChars)
                        kvpSlot.Value.AddToStringBuilder(sb);
                    else
                        sb.Append('[').Append(kvpSlot.Key).Append(']');
                    sb.Append('.');

                    int len = sb.Length;
                    foreach (var key in bits.Values)
                    {
                        sb.Length = len;
                        key.AddToStringBuilder(sb);
                        values[slot++] = new DvText(sb.ToString());
                    }
                }
                Host.Assert(slot == slotLim);

                dst = new VBuffer<DvText>(slotLim, values, dst.Indices);
            }

            protected override Delegate MakeGetter(IRow input, int iinfo, out Action disposer)
            {
                Host.AssertValue(input);
                Host.Assert(0 <= iinfo && iinfo < _infos.Length);
                disposer = null;

                var info = _infos[iinfo];
                if (!info.TypeSrc.IsVector)
                    return MakeGetterOne(input, iinfo);
                return MakeGetterInd(input, iinfo);
            }

            /// <summary>
            /// This is for the scalar case.
            /// </summary>
            private ValueGetter<VBuffer<float>> MakeGetterOne(IRow input, int iinfo)
            {
                Host.AssertValue(input);
                Host.Assert(_infos[iinfo].TypeSrc.IsKey);

                int bitsPerKey = _bitsPerKey[iinfo];
                Host.Assert(bitsPerKey == _types[iinfo].VectorSize);

                int dstLength = _types[iinfo].VectorSize;
                Host.Assert(dstLength > 0);
                input.Schema.TryGetColumnIndex(_infos[iinfo].Source, out int srcCol);
                Host.Assert(srcCol >= 0);
                var getSrc = RowCursorUtils.GetGetterAs<uint>(NumberType.U4, input, srcCol);
                var src = default(uint);
                var bldr = new BufferBuilder<float>(R4Adder.Instance);
                return
                    (ref VBuffer<float> dst) =>
                    {
                        getSrc(ref src);
                        bldr.Reset(bitsPerKey, false);
                        EncodeValueToBinary(bldr, src, bitsPerKey, 0);
                        bldr.GetResult(ref dst);

                        Contracts.Assert(dst.Length == bitsPerKey);
                    };
            }

            /// <summary>
            /// This is for the indicator case - vector input and outputs should be concatenated.
            /// </summary>
            private ValueGetter<VBuffer<float>> MakeGetterInd(IRow input, int iinfo)
            {
                Host.AssertValue(input);
                Host.Assert(_infos[iinfo].TypeSrc.IsVector);
                Host.Assert(_infos[iinfo].TypeSrc.ItemType.IsKey);

                int cv = _infos[iinfo].TypeSrc.VectorSize;
                Host.Assert(cv >= 0);
                input.Schema.TryGetColumnIndex(_infos[iinfo].Source, out int srcCol);
                Host.Assert(srcCol >= 0);
                var getSrc = RowCursorUtils.GetVecGetterAs<uint>(NumberType.U4, input, srcCol);
                var src = default(VBuffer<uint>);
                var bldr = new BufferBuilder<float>(R4Adder.Instance);
                int bitsPerKey = _bitsPerKey[iinfo];
                return
                    (ref VBuffer<float> dst) =>
                    {
                        getSrc(ref src);
                        Host.Check(src.Length == cv || cv == 0);
                        bldr.Reset(src.Length * bitsPerKey, false);

                        int index = 0;
                        foreach (uint value in src.DenseValues())
                        {
                            EncodeValueToBinary(bldr, value, bitsPerKey, index * bitsPerKey);
                            index++;
                        }

                        bldr.GetResult(ref dst);

                        Contracts.Assert(dst.Length == src.Length * bitsPerKey);
                    };
            }

            private void EncodeValueToBinary(BufferBuilder<float> bldr, uint value, int bitsToConsider, int startIndex)
            {
                Contracts.Assert(0 < bitsToConsider && bitsToConsider <= sizeof(uint) * 8);
                Contracts.Assert(startIndex >= 0);

                //Treat missing values, zero, as a special value of all 1s.
                value--;
                while (bitsToConsider > 0)
                    bldr.AddFeature(startIndex++, (value >> --bitsToConsider) & 1U);
            }
        }
    }

    public sealed class KeyToBinaryVectorEstimator : IEstimator<KeyToBinaryVectorTransform>
    {
        private readonly IHost _host;
        private readonly KeyToBinaryVectorTransform.ColumnInfo[] _columns;

        public KeyToBinaryVectorEstimator(IHostEnvironment env, params KeyToBinaryVectorTransform.ColumnInfo[] columns)
        {
            Contracts.CheckValue(env, nameof(env));
            _host = env.Register(nameof(KeyToBinaryVectorEstimator));
            _columns = columns;
        }

        public KeyToBinaryVectorEstimator(IHostEnvironment env, string name, string source = null) :
          this(env, new KeyToBinaryVectorTransform.ColumnInfo(source ?? name, name))
        {
        }

        public SchemaShape GetOutputSchema(SchemaShape inputSchema)
        {
            _host.CheckValue(inputSchema, nameof(inputSchema));
            var result = inputSchema.Columns.ToDictionary(x => x.Name);
            foreach (var colInfo in _columns)
            {
                if (!inputSchema.TryFindColumn(colInfo.Input, out var col))
                    throw _host.ExceptSchemaMismatch(nameof(inputSchema), "input", colInfo.Input);
                if ((col.ItemType.ItemType.RawKind == default) || !(col.ItemType.IsVector || col.ItemType.IsPrimitive))
                    throw _host.ExceptSchemaMismatch(nameof(inputSchema), "input", colInfo.Input);

                var metadata = new List<SchemaShape.Column>();
                if (col.Metadata.TryFindColumn(MetadataUtils.Kinds.KeyValues, out var keyMeta))
                    if (col.Kind != SchemaShape.Column.VectorKind.VariableVector && keyMeta.ItemType.IsText)
                        metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.SlotNames, SchemaShape.Column.VectorKind.Vector, keyMeta.ItemType, false));
                if (col.Kind == SchemaShape.Column.VectorKind.Scalar)
                    metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.IsNormalized, SchemaShape.Column.VectorKind.Scalar, BoolType.Instance, false));
                result[colInfo.Output] = new SchemaShape.Column(colInfo.Output, SchemaShape.Column.VectorKind.Vector, NumberType.R4, false, new SchemaShape(metadata));
            }

            return new SchemaShape(result.Values);
        }

        public KeyToBinaryVectorTransform Fit(IDataView input) => new KeyToBinaryVectorTransform(_host, input, _columns);
    }

}
