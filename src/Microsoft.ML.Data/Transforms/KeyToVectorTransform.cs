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
using Microsoft.ML.Runtime.Model.Onnx;
using Microsoft.ML.Runtime.Model.Pfa;
using Newtonsoft.Json.Linq;

[assembly: LoadableClass(KeyToVectorTransform.Summary, typeof(IDataTransform), typeof(KeyToVectorTransform), typeof(KeyToVectorTransform.Arguments), typeof(SignatureDataTransform),
    "Key To Vector Transform", KeyToVectorTransform.UserName, "KeyToVector", "ToVector", DocName = "transform/KeyToVectorTransform.md")]

[assembly: LoadableClass(KeyToVectorTransform.Summary, typeof(IDataView), typeof(KeyToVectorTransform), null, typeof(SignatureLoadDataTransform),
    "Key To Vector Transform", KeyToVectorTransform.LoaderSignature)]

[assembly: LoadableClass(KeyToVectorTransform.Summary, typeof(KeyToVectorTransform), null, typeof(SignatureLoadModel),
    KeyToVectorTransform.UserName, KeyToVectorTransform.LoaderSignature)]

[assembly: LoadableClass(typeof(IRowMapper), typeof(KeyToVectorTransform), null, typeof(SignatureLoadRowMapper),
   KeyToVectorTransform.UserName, KeyToVectorTransform.LoaderSignature)]

namespace Microsoft.ML.Runtime.Data
{

    public sealed class KeyToVectorTransform : OneToOneTransformerBase
    {
        public abstract class ColumnBase : OneToOneColumn
        {
            [Argument(ArgumentType.AtMostOnce,
                HelpText = "Whether to combine multiple indicator vectors into a single bag vector instead of concatenating them. This is only relevant when the input is a vector.")]
            public bool? Bag;

            protected override bool TryUnparseCore(StringBuilder sb)
            {
                Contracts.AssertValue(sb);
                if (Bag != null)
                    return false;
                return base.TryUnparseCore(sb);
            }

            protected override bool TryUnparseCore(StringBuilder sb, string extra)
            {
                Contracts.AssertValue(sb);
                Contracts.AssertNonEmpty(extra);
                if (Bag != null)
                    return false;
                return base.TryUnparseCore(sb, extra);
            }
        }

        public sealed class Column : ColumnBase
        {
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
                return TryUnparseCore(sb);
            }
        }
        public sealed class Arguments
        {
            [Argument(ArgumentType.Multiple, HelpText = "New column definition(s) (optional form: name:src)", ShortName = "col", SortOrder = 1)]
            public Column[] Column;

            [Argument(ArgumentType.AtMostOnce,
                HelpText = "Whether to combine multiple indicator vectors into a single bag vector instead of concatenating them. This is only relevant when the input is a vector.")]
            public bool Bag = KeyToVectorEstimator.Defaults.Bag;
        }

        public class ColumnInfo
        {
            public readonly string Input;
            public readonly string Output;
            public readonly bool Bag;
            public ColumnInfo(string input, string output, bool bag = KeyToVectorEstimator.Defaults.Bag)
            {
                Input = input;
                Output = output;
                Bag = bag;
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

        private const string RegistrationName = "KeyToVector";

        /// <summary>
        /// _bags indicates whether vector inputs should have their output indicator vectors added
        /// (instead of concatenated). This is faithful to what the user specified in the Arguments
        ///   and is persisted.
        /// </summary>
        private readonly bool[] _bags;

        private static (string input, string output)[] GetColumnPairs(ColumnInfo[] columns)
        {
            Contracts.CheckValue(columns, nameof(columns));
            return columns.Select(x => (x.Input, x.Output)).ToArray();
        }

        //REVIEW: This and static method below need to go to base class as it get created.
        private const string InvalidTypeErrorFormat = "Source column '{0}' has invalid type ('{1}'): {2}.";

        private ColInfo[] CreateInfos(ISchema schema)
        {
            Host.AssertValue(schema);
            var infos = new ColInfo[ColumnPairs.Length];
            for (int i = 0; i < ColumnPairs.Length; i++)
            {
                if (!schema.TryGetColumnIndex(ColumnPairs[i].input, out int colSrc))
                    throw Host.ExceptUserArg(nameof(ColumnPairs), "Source column '{0}' not found", ColumnPairs[i].input);
                var type = schema.GetColumnType(colSrc);
                string reason = TestIsKey(type);
                if (reason != null)
                    throw Host.ExceptUserArg(nameof(ColumnPairs), InvalidTypeErrorFormat, ColumnPairs[i].input, type, reason);
                infos[i] = new ColInfo(ColumnPairs[i].output, ColumnPairs[i].input, type);
            }
            return infos;
        }

        private string TestIsKey(ColumnType type)
        {
            if (type.ItemType.KeyCount > 0)
                return null;
            return "Expected Key type of known cardinality";
        }

        public KeyToVectorTransform(IHostEnvironment env, IDataView input, ColumnInfo[] columns) :
            base(Contracts.CheckRef(env, nameof(env)).Register(RegistrationName), GetColumnPairs(columns))
        {
            var infos = CreateInfos(input.Schema);
            _bags = new bool[infos.Length];
            for (int i = 0; i < infos.Length; i++)
                _bags[i] = columns[i].Bag;
        }

        public const string LoaderSignature = "KeyToVectorTransform";
        public const string UserName = "KeyToVectorTransform";
        internal const string Summary = "Converts a key column to an indicator vector.";

        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                modelSignature: "KEY2VECT",
                verWrittenCur: 0x00010001, // Initial
                verReadableCur: 0x00010001,
                verWeCanReadBack: 0x00010001,
                loaderSignature: LoaderSignature);
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
            //   byte: bag as 0/1
            // for each added column
            //  int: keyCount
            //  int: valueCount
            ctx.Writer.Write(sizeof(float));
            SaveColumns(ctx);

            Host.Assert(_bags.Length == ColumnPairs.Length);
            for (int i = 0; i < _bags.Length; i++)
                ctx.Writer.WriteBoolByte(_bags[i]);
        }

        // Factory method for SignatureLoadModel.
        public static KeyToVectorTransform Create(IHostEnvironment env, ModelLoadContext ctx)
        {
            Contracts.CheckValue(env, nameof(env));
            var host = env.Register(RegistrationName);

            host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel(GetVersionInfo());

            return new KeyToVectorTransform(host, ctx);
        }
        private static ModelLoadContext ReadFloatFromCtx(IHostEnvironment env, ModelLoadContext ctx)
        {
            int cbFloat = ctx.Reader.ReadInt32();
            env.CheckDecode(cbFloat == sizeof(float));
            return ctx;
        }
        private KeyToVectorTransform(IHost host, ModelLoadContext ctx)
          : base(host, ReadFloatFromCtx(host, ctx))
        {
            var columnsLength = ColumnPairs.Length;
            // *** Binary format ***
            // <base>
            // for each added column
            //   byte: bag as 0/1
            // for each added column
            //  int: keyCount
            //  int: valueCount
            _bags = new bool[columnsLength];
            _bags = ctx.Reader.ReadBoolArray(columnsLength);
        }

        public static IDataTransform Create(IHostEnvironment env, IDataView input, params ColumnInfo[] columns) =>
             new KeyToVectorTransform(env, input, columns).MakeDataTransform(input);

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

                    cols[i] = new ColumnInfo(item.Source,
                        item.Name,
                        item.Bag ?? args.Bag);
                };
            }
            return new KeyToVectorTransform(env, input, cols).MakeDataTransform(input);
        }

        // Factory method for SignatureLoadDataTransform.
        public static IDataTransform Create(IHostEnvironment env, ModelLoadContext ctx, IDataView input)
            => Create(env, ctx).MakeDataTransform(input);

        // Factory method for SignatureLoadRowMapper.
        public static IRowMapper Create(IHostEnvironment env, ModelLoadContext ctx, ISchema inputSchema)
            => Create(env, ctx).MakeRowMapper(inputSchema);

        protected override IRowMapper MakeRowMapper(ISchema schema) => new Mapper(this, schema);

        private sealed class Mapper : MapperBase, ISaveAsOnnx, ISaveAsPfa
        {
            private readonly KeyToVectorTransform _parent;
            private readonly ColInfo[] _infos;
            private readonly ColumnType[] _types;

            public Mapper(KeyToVectorTransform parent, ISchema inputSchema)
                : base(parent.Host.Register(nameof(Mapper)), parent, inputSchema)
            {
                _parent = parent;
                _infos = _parent.CreateInfos(inputSchema);
                _types = new ColumnType[_parent.ColumnPairs.Length];
                for (int i = 0; i < _parent.ColumnPairs.Length; i++)
                {
                    ColumnType type;
                    if (_infos[i].TypeSrc.ValueCount == 1)
                        type = new VectorType(NumberType.Float, _infos[i].TypeSrc.ItemType.KeyCount);
                    else
                        type = new VectorType(NumberType.Float, _infos[i].TypeSrc.ValueCount, _infos[i].TypeSrc.ItemType.KeyCount);
                    _types[i] = type;
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

                    ColumnType type;
                    if (_infos[i].TypeSrc.ValueCount == 1)
                        type = new VectorType(NumberType.Float, _infos[i].TypeSrc.ItemType.KeyCount);
                    else
                        type = new VectorType(NumberType.Float, _infos[i].TypeSrc.ValueCount, _infos[i].TypeSrc.ItemType.KeyCount);
                    result[i] = new RowMapperColumnInfo(_parent.ColumnPairs[i].output, _types[i], colMetaInfo);
                }
                return result;
            }

            private void AddMetadata(int i, ColumnMetadataInfo colMetaInfo)
            {
                InputSchema.TryGetColumnIndex(_infos[i].Source, out int srcCol);
                var srcType = _infos[i].TypeSrc;
                var typeNames = InputSchema.GetMetadataTypeOrNull(MetadataUtils.Kinds.KeyValues, srcCol);
                if (typeNames == null || !typeNames.IsKnownSizeVector || !typeNames.ItemType.IsText ||
                    typeNames.VectorSize != _infos[i].TypeSrc.ItemType.KeyCount)
                {
                    typeNames = null;
                }
                if (_parent._bags[i] || _infos[i].TypeSrc.ValueCount == 1)
                {
                    if (typeNames != null)
                    {
                        MetadataUtils.MetadataGetter<VBuffer<DvText>> getter = (int col, ref VBuffer<DvText> dst) =>
                        {
                            InputSchema.GetMetadata(MetadataUtils.Kinds.KeyValues, srcCol, ref dst);
                        };
                        var info = new MetadataInfo<VBuffer<DvText>>(typeNames, getter);
                        colMetaInfo.Add(MetadataUtils.Kinds.SlotNames, info);
                    }
                }
                else
                {
                    var type = new VectorType(NumberType.Float, _infos[i].TypeSrc.ValueCount, _infos[i].TypeSrc.ItemType.KeyCount);
                    if (typeNames != null && type.IsKnownSizeVector)
                    {
                        MetadataUtils.MetadataGetter<VBuffer<DvText>> getter = (int col, ref VBuffer<DvText> dst) =>
                        {
                            GetSlotNames(i, ref dst);
                        };
                        var info = new MetadataInfo<VBuffer<DvText>>(new VectorType(TextType.Instance, type), getter);
                        colMetaInfo.Add(MetadataUtils.Kinds.SlotNames, info);
                    }
                }

                if (!_parent._bags[i] && srcType.ValueCount > 0)
                {
                    MetadataUtils.MetadataGetter<VBuffer<DvInt4>> getter = (int col, ref VBuffer<DvInt4> dst) =>
                    {
                        GetCategoricalSlotRanges(i, ref dst);
                    };
                    var info = new MetadataInfo<VBuffer<DvInt4>>(MetadataUtils.GetCategoricalType(_infos[i].TypeSrc.ValueCount), getter);
                    colMetaInfo.Add(MetadataUtils.Kinds.CategoricalSlotRanges, info);
                }

                if (!_parent._bags[i] || srcType.ValueCount == 1)
                {
                    MetadataUtils.MetadataGetter<DvBool> getter = (int col, ref DvBool dst) =>
                    {
                        dst = true;
                    };
                    var info = new MetadataInfo<DvBool>(BoolType.Instance, getter);
                    colMetaInfo.Add(MetadataUtils.Kinds.IsNormalized, info);
                }
            }

            // Combines source key names and slot names to produce final slot names.
            private void GetSlotNames(int iinfo, ref VBuffer<DvText> dst)
            {
                Host.Assert(0 <= iinfo && iinfo < _infos.Length);
                var type = new VectorType(NumberType.Float, _infos[iinfo].TypeSrc.ValueCount, _infos[iinfo].TypeSrc.ItemType.KeyCount);
                Host.Assert(type.IsKnownSizeVector);

                // Size one should have been treated the same as Bag (by the caller).
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

                int keyCount = typeSrc.ItemType.ItemType.KeyCount;
                int slotLim = type.VectorSize;
                Host.Assert(slotLim == (long)typeSrc.VectorSize * keyCount);

                // Get the source key names, in an array (since we will use them multiple times).
                var namesKeySrc = default(VBuffer<DvText>);
                InputSchema.GetMetadata(MetadataUtils.Kinds.KeyValues, srcCol, ref namesKeySrc);
                Host.Check(namesKeySrc.Length == keyCount);
                var keys = new DvText[keyCount];
                namesKeySrc.CopyTo(keys);

                var values = dst.Values;
                if (Utils.Size(values) < slotLim)
                    values = new DvText[slotLim];

                var sb = new StringBuilder();
                int slot = 0;
                foreach (var kvpSlot in namesSlotSrc.Items(all: true))
                {
                    Contracts.Assert(slot == (long)kvpSlot.Key * keyCount);
                    sb.Clear();
                    if (kvpSlot.Value.HasChars)
                        kvpSlot.Value.AddToStringBuilder(sb);
                    else
                        sb.Append('[').Append(kvpSlot.Key).Append(']');
                    sb.Append('.');

                    int len = sb.Length;
                    foreach (var key in keys)
                    {
                        sb.Length = len;
                        key.AddToStringBuilder(sb);
                        values[slot++] = new DvText(sb.ToString());
                    }
                }
                Host.Assert(slot == slotLim);

                dst = new VBuffer<DvText>(slotLim, values, dst.Indices);
            }

            private void GetCategoricalSlotRanges(int iinfo, ref VBuffer<DvInt4> dst)
            {
                Host.Assert(0 <= iinfo && iinfo < _infos.Length);

                var info = _infos[iinfo];

                Host.Assert(info.TypeSrc.ValueCount > 0);

                DvInt4[] ranges = new DvInt4[info.TypeSrc.ValueCount * 2];
                int size = info.TypeSrc.ItemType.KeyCount;

                ranges[0] = 0;
                ranges[1] = size - 1;
                for (int i = 2; i < ranges.Length; i += 2)
                {
                    ranges[i] = ranges[i - 1] + 1;
                    ranges[i + 1] = ranges[i] + size - 1;
                }

                dst = new VBuffer<DvInt4>(ranges.Length, ranges);
            }

            protected override Delegate MakeGetter(IRow input, int iinfo, out Action disposer)
            {
                Host.AssertValue(input);
                Host.Assert(0 <= iinfo && iinfo < _infos.Length);
                disposer = null;

                var info = _infos[iinfo];
                if (!info.TypeSrc.IsVector)
                    return MakeGetterOne(input, iinfo);
                if (_parent._bags[iinfo])
                    return MakeGetterBag(input, iinfo);
                return MakeGetterInd(input, iinfo);
            }

            /// <summary>
            /// This is for the singleton case. This should be equivalent to both Bag and Ord over
            /// a vector of size one.
            /// </summary>
            private ValueGetter<VBuffer<float>> MakeGetterOne(IRow input, int iinfo)
            {
                Host.AssertValue(input);
                Host.Assert(_infos[iinfo].TypeSrc.IsKey);
                Host.Assert(_infos[iinfo].TypeSrc.KeyCount == _types[iinfo].VectorSize);

                int size = _infos[iinfo].TypeSrc.KeyCount;
                Host.Assert(size > 0);
                input.Schema.TryGetColumnIndex(_infos[iinfo].Source, out int srcCol);
                var getSrc = RowCursorUtils.GetGetterAs<uint>(NumberType.U4, input, srcCol);
                var src = default(uint);
                return
                    (ref VBuffer<float> dst) =>
                    {
                        getSrc(ref src);
                        if (src == 0 || src > size)
                        {
                            dst = new VBuffer<float>(size, 0, dst.Values, dst.Indices);
                            return;
                        }

                        var values = dst.Values;
                        var indices = dst.Indices;
                        if (Utils.Size(values) < 1)
                            values = new float[1];
                        if (Utils.Size(indices) < 1)
                            indices = new int[1];
                        values[0] = 1;
                        indices[0] = (int)src - 1;

                        dst = new VBuffer<float>(size, 1, values, indices);
                    };
            }

            /// <summary>
            /// This is for the bagging case - vector input and outputs should be added.
            /// </summary>
            private ValueGetter<VBuffer<float>> MakeGetterBag(IRow input, int iinfo)
            {
                Host.AssertValue(input);
                Host.Assert(_infos[iinfo].TypeSrc.IsVector);
                Host.Assert(_infos[iinfo].TypeSrc.ItemType.IsKey);
                Host.Assert(_parent._bags[iinfo]);
                Host.Assert(_infos[iinfo].TypeSrc.ItemType.KeyCount == _types[iinfo].VectorSize);

                var info = _infos[iinfo];
                int size = info.TypeSrc.ItemType.KeyCount;
                Host.Assert(size > 0);

                int cv = info.TypeSrc.VectorSize;
                Host.Assert(cv >= 0);
                input.Schema.TryGetColumnIndex(info.Source, out int srcCol);
                var getSrc = RowCursorUtils.GetVecGetterAs<uint>(NumberType.U4, input, srcCol);
                var src = default(VBuffer<uint>);
                var bldr = BufferBuilder<float>.CreateDefault();
                return
                    (ref VBuffer<float> dst) =>
                    {
                        bldr.Reset(size, false);

                        getSrc(ref src);
                        Host.Check(cv == 0 || src.Length == cv);

                        // The indices are irrelevant in the bagging case.
                        var values = src.Values;
                        int count = src.Count;
                        for (int slot = 0; slot < count; slot++)
                        {
                            uint key = values[slot] - 1;
                            if (key < size)
                                bldr.AddFeature((int)key, 1);
                        }

                        bldr.GetResult(ref dst);
                    };
            }

            /// <summary>
            /// This is for the indicator (non-bagging) case - vector input and outputs should be concatenated.
            /// </summary>
            private ValueGetter<VBuffer<float>> MakeGetterInd(IRow input, int iinfo)
            {
                Host.AssertValue(input);
                Host.Assert(_infos[iinfo].TypeSrc.IsVector);
                Host.Assert(_infos[iinfo].TypeSrc.ItemType.IsKey);
                Host.Assert(!_parent._bags[iinfo]);

                var info = _infos[iinfo];
                int size = info.TypeSrc.ItemType.KeyCount;
                Host.Assert(size > 0);

                int cv = info.TypeSrc.VectorSize;
                Host.Assert(cv >= 0);
                Host.Assert(_types[iinfo].VectorSize == size * cv);
                input.Schema.TryGetColumnIndex(info.Source, out int srcCol);
                var getSrc = RowCursorUtils.GetVecGetterAs<uint>(NumberType.U4, input, srcCol);
                var src = default(VBuffer<uint>);
                return
                    (ref VBuffer<float> dst) =>
                    {
                        getSrc(ref src);
                        int lenSrc = src.Length;
                        Host.Check(lenSrc == cv || cv == 0);

                        // Since we generate values in order, no need for a builder.
                        var valuesDst = dst.Values;
                        var indicesDst = dst.Indices;

                        int lenDst = checked(size * lenSrc);
                        int cntSrc = src.Count;
                        if (Utils.Size(valuesDst) < cntSrc)
                            valuesDst = new float[cntSrc];
                        if (Utils.Size(indicesDst) < cntSrc)
                            indicesDst = new int[cntSrc];

                        var values = src.Values;
                        int count = 0;
                        if (src.IsDense)
                        {
                            Host.Assert(lenSrc == cntSrc);
                            for (int slot = 0; slot < cntSrc; slot++)
                            {
                                Host.Assert(count < cntSrc);
                                uint key = values[slot] - 1;
                                if (key >= (uint)size)
                                    continue;
                                valuesDst[count] = 1;
                                indicesDst[count++] = slot * size + (int)key;
                            }
                        }
                        else
                        {
                            var indices = src.Indices;
                            for (int islot = 0; islot < cntSrc; islot++)
                            {
                                Host.Assert(count < cntSrc);
                                uint key = values[islot] - 1;
                                if (key >= (uint)size)
                                    continue;
                                valuesDst[count] = 1;
                                indicesDst[count++] = indices[islot] * size + (int)key;
                            }
                        }
                        dst = new VBuffer<float>(lenDst, count, valuesDst, indicesDst);
                    };
            }

            public bool CanSaveOnnx => true;

            public bool CanSavePfa => true;

            public void SaveAsOnnx(OnnxContext ctx)
            {
                Host.CheckValue(ctx, nameof(ctx));

                for (int iinfo = 0; iinfo < _infos.Length; ++iinfo)
                {
                    ColInfo info = _infos[iinfo];
                    string sourceColumnName = info.Source;
                    if (!ctx.ContainsColumn(sourceColumnName))
                    {
                        ctx.RemoveColumn(info.Name, false);
                        continue;
                    }

                    if (!SaveAsOnnxCore(ctx, iinfo, info, ctx.GetVariableName(sourceColumnName),
                        ctx.AddIntermediateVariable(_types[iinfo], info.Name)))
                    {
                        ctx.RemoveColumn(info.Name, true);
                    }
                }
            }

            public void SaveAsPfa(BoundPfaContext ctx)
            {
                Host.CheckValue(ctx, nameof(ctx));

                var toHide = new List<string>();
                var toDeclare = new List<KeyValuePair<string, JToken>>();

                for (int iinfo = 0; iinfo < _infos.Length; ++iinfo)
                {
                    var info = _infos[iinfo];
                    var srcName = info.Source;
                    string srcToken = ctx.TokenOrNullForName(srcName);
                    if (srcToken == null)
                    {
                        toHide.Add(info.Name);
                        continue;
                    }
                    var result = SaveAsPfaCore(ctx, iinfo, info, srcToken);
                    if (result == null)
                    {
                        toHide.Add(info.Name);
                        continue;
                    }
                    toDeclare.Add(new KeyValuePair<string, JToken>(info.Name, result));
                }
                ctx.Hide(toHide.ToArray());
                ctx.DeclareVar(toDeclare.ToArray());
            }

            private JToken SaveAsPfaCore(BoundPfaContext ctx, int iinfo, ColInfo info, JToken srcToken)
            {
                Contracts.AssertValue(ctx);
                Contracts.Assert(0 <= iinfo && iinfo < _infos.Length);
                Contracts.Assert(_infos[iinfo] == info);
                Contracts.AssertValue(srcToken);
                Contracts.Assert(CanSavePfa);

                int keyCount = info.TypeSrc.ItemType.KeyCount;
                Host.Assert(keyCount > 0);
                // If the input type is scalar, we can just use the fanout function.
                if (!info.TypeSrc.IsVector)
                    return PfaUtils.Call("cast.fanoutDouble", srcToken, 0, keyCount, false);

                JToken arrType = PfaUtils.Type.Array(PfaUtils.Type.Double);
                if (_parent._bags[iinfo] || info.TypeSrc.ValueCount == 1)
                {
                    // The concatenation case. We can still use fanout, but we just append them all together.
                    return PfaUtils.Call("a.flatMap", srcToken,
                        PfaContext.CreateFuncBlock(new JArray() { PfaUtils.Param("k", PfaUtils.Type.Int) },
                        arrType, PfaUtils.Call("cast.fanoutDouble", "k", 0, keyCount, false)));
                }

                // The bag case, while the most useful, is the most elaborate and difficult: we create
                // an all-zero array and then add items to it.
                const string funcName = "keyToVecUpdate";
                if (!ctx.Pfa.ContainsFunc(funcName))
                {
                    var toFunc = PfaContext.CreateFuncBlock(
                        new JArray() { PfaUtils.Param("v", PfaUtils.Type.Double) }, PfaUtils.Type.Double,
                        PfaUtils.Call("+", "v", 1));

                    ctx.Pfa.AddFunc(funcName,
                        new JArray(PfaUtils.Param("a", arrType), PfaUtils.Param("i", PfaUtils.Type.Int)),
                        arrType, PfaUtils.If(PfaUtils.Call(">=", "i", 0),
                        PfaUtils.Index("a", "i").AddReturn("to", toFunc), "a"));
                }

                return PfaUtils.Call("a.fold", srcToken,
                    PfaUtils.Call("cast.fanoutDouble", -1, 0, keyCount, false), PfaUtils.FuncRef("u." + funcName));
            }

            private bool SaveAsOnnxCore(OnnxContext ctx, int iinfo, ColInfo info, string srcVariableName, string dstVariableName)
            {
                string opType = "OneHotEncoder";
                var node = ctx.CreateNode(opType, srcVariableName, dstVariableName, ctx.GetNodeName(opType));
                node.AddAttribute("cats_int64s", Enumerable.Range(1, info.TypeSrc.ItemType.KeyCount).Select(x => (long)x));
                node.AddAttribute("zeros", true);
                return true;
            }
        }
    }

    public sealed class KeyToVectorEstimator : IEstimator<KeyToVectorTransform>
    {
        private readonly IHost _host;
        private readonly KeyToVectorTransform.ColumnInfo[] _columns;
        public static class Defaults
        {
            public const bool Bag = false;
        }

        public KeyToVectorEstimator(IHostEnvironment env, params KeyToVectorTransform.ColumnInfo[] columns)
        {
            Contracts.CheckValue(env, nameof(env));
            _host = env.Register(nameof(KeyToVectorEstimator));
            _columns = columns;
        }

        public KeyToVectorEstimator(IHostEnvironment env, string name, string source = null, bool bag = Defaults.Bag) :
            this(env, new KeyToVectorTransform.ColumnInfo(source ?? name, name, bag))
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
                    if (col.Kind != SchemaShape.Column.VectorKind.VariableVector && col.ItemType.IsText)
                        metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.SlotNames, SchemaShape.Column.VectorKind.Vector, keyMeta.ItemType, false));
                if (!colInfo.Bag && (col.Kind == SchemaShape.Column.VectorKind.Scalar || col.Kind == SchemaShape.Column.VectorKind.Vector))
                    metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.CategoricalSlotRanges, SchemaShape.Column.VectorKind.Vector, NumberType.I4, false));
                if (!colInfo.Bag || (col.Kind == SchemaShape.Column.VectorKind.Scalar))
                    metadata.Add(new SchemaShape.Column(MetadataUtils.Kinds.IsNormalized, SchemaShape.Column.VectorKind.Scalar, BoolType.Instance, false));

                result[colInfo.Output] = new SchemaShape.Column(colInfo.Output, SchemaShape.Column.VectorKind.Vector, NumberType.R4, false, new SchemaShape(metadata));
            }

            return new SchemaShape(result.Values);
        }

        public KeyToVectorTransform Fit(IDataView input) => new KeyToVectorTransform(_host, input, _columns);
    }
}
