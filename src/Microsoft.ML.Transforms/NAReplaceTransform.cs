// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using System.Linq;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Data.IO;
using Microsoft.ML.Runtime.Data.Conversion;
using Microsoft.ML.Runtime.EntryPoints;
using Microsoft.ML.Runtime.Internal.Utilities;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.Runtime.Model.Onnx;
using Microsoft.ML.Core.Data;

[assembly: LoadableClass(NAReplaceTransform.Summary, typeof(IDataTransform), typeof(NAReplaceTransform), typeof(NAReplaceTransform.Arguments), typeof(SignatureDataTransform),
    NAReplaceTransform.FriendlyName, NAReplaceTransform.LoadName, "NAReplace", NAReplaceTransform.ShortName, DocName = "transform/NAHandle.md")]

[assembly: LoadableClass(NAReplaceTransform.Summary, typeof(IDataView), typeof(NAReplaceTransform), null, typeof(SignatureLoadDataTransform),
    NAReplaceTransform.FriendlyName, NAReplaceTransform.LoadName)]

[assembly: LoadableClass(NAReplaceTransform.Summary, typeof(NAReplaceTransform), null, typeof(SignatureLoadModel),
    NAReplaceTransform.FriendlyName, NAReplaceTransform.LoadName)]

[assembly: LoadableClass(typeof(IRowMapper), typeof(NAReplaceTransform), null, typeof(SignatureLoadRowMapper),
   NAReplaceTransform.FriendlyName, NAReplaceTransform.LoadName)]

namespace Microsoft.ML.Runtime.Data
{
    public sealed partial class NAReplaceTransform : OneToOneTransformerBase
    {
        //IVAN: CLEAN IT
        public enum ReplacementKind
        {
            // REVIEW: What should the full list of options for this transform be?
            DefaultValue,
            Mean,
            Minimum,
            Maximum,
            SpecifiedValue,

            [HideEnumValue]
            Def = DefaultValue,
            [HideEnumValue]
            Default = DefaultValue,
            [HideEnumValue]
            Min = Minimum,
            [HideEnumValue]
            Max = Maximum,

            [HideEnumValue]
            Val = SpecifiedValue,
            [HideEnumValue]
            Value = SpecifiedValue,
        }

        // REVIEW: Need to add support for imputation modes for replacement values:
        // *default: use default value
        // *custom: use replacementValue string
        // *mean: use domain value closest to the mean
        // Potentially also min/max; probably will not include median due to its relatively low value and high computational cost.
        // Note: Will need to support different replacement values for different slots to implement this.
        public sealed class Column : OneToOneColumn
        {
            // REVIEW: Should flexibility for different replacement values for slots be introduced?
            [Argument(ArgumentType.AtMostOnce, HelpText = "Replacement value for NAs (uses default value if not given)", ShortName = "rep")]
            public string ReplacementString;

            [Argument(ArgumentType.AtMostOnce, HelpText = "The replacement method to utilize")]
            public ReplacementKind? Kind;

            // REVIEW: The default is to perform imputation by slot. If the input column is an unknown size vector type, then imputation
            // will be performed across columns. Should the default be changed/an imputation method required?
            [Argument(ArgumentType.AtMostOnce, HelpText = "Whether to impute values by slot")]
            public bool? Slot;

            public static Column Parse(string str)
            {
                var res = new Column();
                if (res.TryParse(str))
                    return res;
                return null;
            }

            protected override bool TryParse(string str)
            {
                // We accept N:R:S where N is the new column name, R is the replacement string,
                // and S is source column names.
                return base.TryParse(str, out ReplacementString);
            }

            public bool TryUnparse(StringBuilder sb)
            {
                Contracts.AssertValue(sb);
                if (Kind != null || Slot != null)
                    return false;
                if (ReplacementString == null)
                    return TryUnparseCore(sb);

                return TryUnparseCore(sb, ReplacementString);
            }
        }

        public sealed class Arguments : TransformInputBase
        {
            [Argument(ArgumentType.Multiple | ArgumentType.Required, HelpText = "New column definition(s) (optional form: name:rep:src)", ShortName = "col", SortOrder = 1)]
            public Column[] Column;

            [Argument(ArgumentType.AtMostOnce, HelpText = "The replacement method to utilize", ShortName = "kind")]
            public ReplacementKind ReplacementKind = ReplacementKind.DefaultValue;

            // Specifying by-slot imputation for vectors of unknown size will cause a warning, and the imputation will be global.
            [Argument(ArgumentType.AtMostOnce, HelpText = "Whether to impute values by slot", ShortName = "slot")]
            public bool ImputeBySlot = true;
        }

        public const string LoadName = "NAReplaceTransform";
        private static VersionInfo GetVersionInfo()
        {
            return new VersionInfo(
                // REVIEW: temporary name
                modelSignature: "NAREP TF",
                // verWrittenCur: 0x00010001, // Initial
                verWrittenCur: 0x0010002, // Added imputation methods.
                verReadableCur: 0x00010002,
                verWeCanReadBack: 0x00010001,
                loaderSignature: LoadName);
        }

        internal const string Summary = "Create an output column of the same type and size of the input column, where missing values "
         + "are replaced with either the default value or the mean/min/max value (for non-text columns only).";

        internal const string FriendlyName = "NA Replace Transform";
        internal const string ShortName = "NARep";

        private static string TestType(ColumnType type)
        {
            // Item type must have an NA value that exists and is not equal to its default value.
            Func<ColumnType, string> func = TestType<int>;
            var meth = func.GetMethodInfo().GetGenericMethodDefinition().MakeGenericMethod(type.ItemType.RawType);
            return (string)meth.Invoke(null, new object[] { type.ItemType });
        }

        private static string TestType<T>(ColumnType type)
        {
            Contracts.Assert(type.ItemType.RawType == typeof(T));
            RefPredicate<T> isNA;
            if (!Conversions.Instance.TryGetIsNAPredicate(type.ItemType, out isNA))
            {
                return string.Format("Type '{0}' is not supported by {1} since it doesn't have an NA value",
                    type, LoadName);
            }
            var t = default(T);
            if (isNA(ref t))
            {
                // REVIEW: Key values will be handled in a "new key value" transform.
                return string.Format("Type '{0}' is not supported by {1} since its NA value is equivalent to its default value",
                    type, LoadName);
            }
            return null;
        }

        public class ColumnInfo
        {
            public readonly string Input;
            public readonly string Output;
            public readonly bool ImputeBySlot;
            public readonly ReplacementKind Kind;

            public ColumnInfo(string input, string output, ReplacementKind kind = ReplacementKind.DefaultValue, bool imputeBySlot = true)
            {
                Input = input;
                Output = output;
                ImputeBySlot = imputeBySlot;
                Kind = kind;
            }
            internal string ReplacementString { get; set; }
        }

        private static (string input, string output)[] GetColumnPairs(ColumnInfo[] columns)
        {
            Contracts.CheckValue(columns, nameof(columns));
            return columns.Select(x => (x.Input, x.Output)).ToArray();
        }

        ///IVAN: move to mapper.
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

        // The output column types, parallel to Infos.
        private readonly ColumnType[] _types;

        // The replacementValues for the columns, parallel to Infos.
        // The elements of this array can be either primitive values or arrays of primitive values. When replacing a scalar valued column in Infos,
        // this array will hold a primitive value. When replacing a vector valued column in Infos, this array will either hold a primitive
        // value, indicating that NAs in all slots will be replaced with this value, or an array of primitives holding the value that each slot
        // will have its NA values replaced with respectively. The case with an array of primitives can only occur when dealing with a
        // vector of known size.
        private readonly object[] _repValues;

        // Marks if the replacement values in given slots of _repValues are the default value.
        // REVIEW: Currently these arrays are constructed on load but could be changed to being constructed lazily.
        private readonly BitArray[] _repIsDefault;

        protected override void CheckInputColumn(ISchema inputSchema, int col, int srcCol)
        {
            var type = inputSchema.GetColumnType(srcCol);
            string reason = TestType(type);
            if (reason != null)
                //IVAN: not sure about schema mismatch
                throw Host.ExceptSchemaMismatch(nameof(inputSchema), "input", ColumnPairs[col].input, reason, type.ToString());
        }

        public NAReplaceTransform(IHostEnvironment env, IDataView input, params ColumnInfo[] columns)
            : base(Contracts.CheckRef(env, nameof(env)).Register(nameof(NAReplaceTransform)), GetColumnPairs(columns))
        {
            // Validate input schema.
            GetReplacementValues(input, columns, out _repValues, out _repIsDefault, out _types);
        }

        private NAReplaceTransform(IHost host, ModelLoadContext ctx)
         : base(host, ctx)
        {
            var columnsLength = ColumnPairs.Length;
            _repValues = new object[columnsLength];
            _repIsDefault = new BitArray[columnsLength];
            _types = new ColumnType[columnsLength];
            var saver = new BinarySaver(Host, new BinarySaver.Arguments());
            for (int i = 0; i < columnsLength; i++)
            {
                if (!saver.TryLoadTypeAndValue(ctx.Reader.BaseStream, out ColumnType savedType, out object repValue))
                    throw Host.ExceptDecode();
                _types[i] = savedType;
                if (savedType.IsVector)
                {
                    // REVIEW: The current implementation takes the serialized VBuffer, densifies it, and stores the values array.
                    // It might be of value to consider storing the VBUffer in order to possibly benefit from sparsity. However, this would
                    // necessitate a reimplementation of the FillValues code to accomodate sparse VBuffers.
                    object[] args = new object[] { repValue, _types[i], i };
                    Func<VBuffer<int>, ColumnType, int, int[]> func = GetValuesArray<int>;
                    var meth = func.GetMethodInfo().GetGenericMethodDefinition().MakeGenericMethod(savedType.ItemType.RawType);
                    _repValues[i] = meth.Invoke(this, args);
                }
                else
                    _repValues[i] = repValue;

                Host.Assert(repValue.GetType() == _types[i].RawType || repValue.GetType() == _types[i].ItemType.RawType);
            }
        }

        private T[] GetValuesArray<T>(VBuffer<T> src, ColumnType srcType, int iinfo)
        {
            Host.Assert(srcType.IsVector);
            Host.Assert(srcType.VectorSize == src.Length);
            VBufferUtils.Densify<T>(ref src);
            RefPredicate<T> defaultPred = Conversions.Instance.GetIsDefaultPredicate<T>(srcType.ItemType);
            _repIsDefault[iinfo] = new BitArray(srcType.VectorSize);
            for (int slot = 0; slot < src.Length; slot++)
            {
                if (defaultPred(ref src.Values[slot]))
                    _repIsDefault[iinfo][slot] = true;
            }
            T[] valReturn = src.Values;
            Array.Resize<T>(ref valReturn, srcType.VectorSize);
            Host.Assert(valReturn.Length == src.Length);
            return valReturn;
        }

        /// <summary>
        /// Fill the repValues array with the correct replacement values based on the user-given replacement kinds.
        /// Vectors default to by-slot imputation unless otherwise specified, except for unknown sized vectors
        /// which force across-slot imputation.
        /// </summary>
        private void GetReplacementValues(IDataView input, ColumnInfo[] columns, out object[] repValues, out BitArray[] slotIsDefault, out ColumnType[] types)
        {
            repValues = new object[columns.Length];
            slotIsDefault = new BitArray[columns.Length];
            types = new ColumnType[columns.Length];
            var sources = new int[columns.Length];
            ReplacementKind?[] imputationModes = new ReplacementKind?[columns.Length];

            List<int> columnsToImpute = null;
            // REVIEW: Would like to get rid of the sourceColumns list but seems to be the best way to provide
            // the cursor with what columns to cursor through.
            HashSet<int> sourceColumns = null;
            for (int iinfo = 0; iinfo < columns.Length; iinfo++)
            {
                input.Schema.TryGetColumnIndex(columns[iinfo].Input, out int colSrc);
                sources[iinfo] = colSrc;
                var type = input.Schema.GetColumnType(colSrc);
                if (type.IsVector)
                    type = new VectorType(type.ItemType.AsPrimitive, type.AsVector);
                types[iinfo] = type;
                switch (columns[iinfo].Kind)
                {
                    //IVAN: what to do with you?
                    //case ReplacementKind.SpecifiedValue:
                    //    repValues[iinfo] = GetSpecifiedValue(args.Column[iinfo].ReplacementString, _types[iinfo], _isNAs[iinfo]);
                    //    break;
                    case ReplacementKind.DefaultValue:
                        repValues[iinfo] = GetDefault(type);
                        break;
                    case ReplacementKind.Mean:
                    case ReplacementKind.Minimum:
                    case ReplacementKind.Maximum:
                        if (!type.ItemType.IsNumber && !type.ItemType.IsTimeSpan && !type.ItemType.IsDateTime)
                            throw Host.Except("Cannot perform mean imputations on non-numeric '{0}'", type.ItemType);
                        imputationModes[iinfo] = columns[iinfo].Kind;
                        Utils.Add(ref columnsToImpute, iinfo);
                        Utils.Add(ref sourceColumns, colSrc);
                        break;
                    default:
                        Host.Assert(false);
                        throw Host.Except("Internal error, undefined ReplacementKind '{0}' assigned in NAReplaceTransform.", columns[iinfo].Kind);
                }
            }

            // Exit if there are no columns needing a replacement value imputed.
            if (Utils.Size(columnsToImpute) == 0)
                return;

            // Impute values.
            using (var ch = Host.Start("Computing Statistics"))
            using (var cursor = input.GetRowCursor(sourceColumns.Contains))
            {
                StatAggregator[] statAggregators = new StatAggregator[columnsToImpute.Count];
                for (int ii = 0; ii < columnsToImpute.Count; ii++)
                {
                    int iinfo = columnsToImpute[ii];
                    bool bySlot = columns[ii].ImputeBySlot;
                    if (types[iinfo].IsVector && !types[iinfo].IsKnownSizeVector && bySlot)
                    {
                        ch.Warning("By-slot imputation can not be done on variable-length column");
                        bySlot = false;
                    }

                    statAggregators[ii] = CreateStatAggregator(ch, types[iinfo], imputationModes[iinfo], bySlot,
                        cursor, sources[iinfo]);
                }

                while (cursor.MoveNext())
                {
                    for (int ii = 0; ii < statAggregators.Length; ii++)
                        statAggregators[ii].ProcessRow();
                }

                for (int ii = 0; ii < statAggregators.Length; ii++)
                    repValues[columnsToImpute[ii]] = statAggregators[ii].GetStat();

                ch.Done();
            }

            // Construct the slotIsDefault bit arrays.
            for (int ii = 0; ii < columnsToImpute.Count; ii++)
            {
                int slot = columnsToImpute[ii];
                if (repValues[slot] is Array)
                {
                    Func<ColumnType, int[], BitArray> func = ComputeDefaultSlots<int>;
                    var meth = func.GetMethodInfo().GetGenericMethodDefinition().MakeGenericMethod(types[slot].ItemType.RawType);
                    slotIsDefault[slot] = (BitArray)meth.Invoke(this, new object[] { types[slot], repValues[slot] });
                }
            }
        }

        private BitArray ComputeDefaultSlots<T>(ColumnType type, T[] values)
        {
            Host.Assert(values.Length == type.VectorSize);
            BitArray defaultSlots = new BitArray(values.Length);
            RefPredicate<T> defaultPred = Conversions.Instance.GetIsDefaultPredicate<T>(type.ItemType);
            for (int slot = 0; slot < values.Length; slot++)
            {
                if (defaultPred(ref values[slot]))
                    defaultSlots[slot] = true;
            }
            return defaultSlots;
        }

        private object GetDefault(ColumnType type)
        {
            Func<object> func = GetDefault<int>;
            var meth = func.GetMethodInfo().GetGenericMethodDefinition().MakeGenericMethod(type.ItemType.RawType);
            return meth.Invoke(this, null);
        }

        private object GetDefault<T>()
        {
            return default(T);
        }

        // Factory method for SignatureDataTransform.
        public static IDataTransform Create(IHostEnvironment env, Arguments args, IDataView input)
        {
            Contracts.CheckValue(env, nameof(env));
            env.CheckValue(args, nameof(args));
            env.CheckValue(input, nameof(input));

            env.CheckValue(args.Column, nameof(args.Column));
            var cols = new ColumnInfo[args.Column.Length];
            for (int i = 0; i < cols.Length; i++)
            {
                var item = args.Column[i];
                var kind = item.Kind ?? args.ReplacementKind;
                if (!Enum.IsDefined(typeof(ReplacementKind), kind))
                    throw env.ExceptUserArg(nameof(args.ReplacementKind), "Undefined sorting criteria '{0}' detected for column '{1}'", kind, item.Name);

                cols[i] = new ColumnInfo(item.Source,
                    item.Name,
                    item.Kind ?? args.ReplacementKind,
                    item.Slot ?? args.ImputeBySlot);
                cols[i].ReplacementString = item.ReplacementString;
            };
            return new NAReplaceTransform(env, input, cols).MakeDataTransform(input);
        }

        public static IDataTransform Create(IHostEnvironment env, IDataView input, params ColumnInfo[] columns)
        {
            return new NAReplaceTransform(env, input, columns).MakeDataTransform(input);
        }

        // Factory method for SignatureLoadModel.
        public static NAReplaceTransform Create(IHostEnvironment env, ModelLoadContext ctx)
        {
            Contracts.CheckValue(env, nameof(env));
            var host = env.Register(LoadName);

            host.CheckValue(ctx, nameof(ctx));
            ctx.CheckAtModel(GetVersionInfo());

            return new NAReplaceTransform(host, ctx);
        }

        // Factory method for SignatureLoadDataTransform.
        public static IDataTransform Create(IHostEnvironment env, ModelLoadContext ctx, IDataView input)
            => Create(env, ctx).MakeDataTransform(input);

        // Factory method for SignatureLoadRowMapper.
        public static IRowMapper Create(IHostEnvironment env, ModelLoadContext ctx, ISchema inputSchema)
            => Create(env, ctx).MakeRowMapper(inputSchema);

        private VBuffer<T> CreateVBuffer<T>(T[] array)
        {
            Host.AssertValue(array);
            return new VBuffer<T>(array.Length, array);
        }

        private void WriteTypeAndValue<T>(Stream stream, BinarySaver saver, ColumnType type, T rep)
        {
            Host.AssertValue(stream);
            Host.AssertValue(saver);
            Host.Assert(type.RawType == typeof(T) || type.ItemType.RawType == typeof(T));

            int bytesWritten;
            if (!saver.TryWriteTypeAndValue<T>(stream, type, ref rep, out bytesWritten))
                throw Host.Except("We do not know how to serialize terms of type '{0}'", type);
        }

        public override void Save(ModelSaveContext ctx)
        {
            Host.CheckValue(ctx, nameof(ctx));

            ctx.CheckAtModel();
            ctx.SetVersionInfo(GetVersionInfo());

            SaveColumns(ctx);
            var saver = new BinarySaver(Host, new BinarySaver.Arguments());
            for (int iinfo = 0; iinfo < _types.Length; iinfo++)
            {
                var repValue = _repValues[iinfo];
                var repType = _types[iinfo].ItemType;
                if (_repIsDefault[iinfo] != null)
                {
                    Host.Assert(repValue is Array);
                    Func<int[], VBuffer<int>> function = CreateVBuffer<int>;
                    var method = function.GetMethodInfo().GetGenericMethodDefinition().MakeGenericMethod(_types[iinfo].ItemType.RawType);
                    repValue = method.Invoke(this, new object[] { _repValues[iinfo] });
                    repType = _types[iinfo];
                }
                Host.Assert(!(repValue is Array));
                object[] args = new object[] { ctx.Writer.BaseStream, saver, repType, repValue };
                Action<Stream, BinarySaver, ColumnType, int> func = WriteTypeAndValue<int>;
                Host.Assert(repValue.GetType() == _types[iinfo].RawType || repValue.GetType() == _types[iinfo].ItemType.RawType);
                var meth = func.GetMethodInfo().GetGenericMethodDefinition().MakeGenericMethod(repValue.GetType());
                meth.Invoke(this, args);
            }
        }

        protected override IRowMapper MakeRowMapper(ISchema schema)
            => new Mapper(this, schema);

        private sealed class Mapper : MapperBase, ISaveAsOnnx
        {
            private readonly NAReplaceTransform _parent;
            private readonly ColInfo[] _infos;
            // The isNA delegates, parallel to Infos.
            private readonly Delegate[] _isNAs;
            public bool CanSaveOnnx => true;

            public Mapper(NAReplaceTransform parent, ISchema inputSchema)
             : base(parent.Host.Register(nameof(Mapper)), parent, inputSchema)
            {
                _parent = parent;
                _infos = CreateInfos(inputSchema);
                _isNAs = new Delegate[_parent.ColumnPairs.Length];
                for (int i = 0; i < _parent.ColumnPairs.Length; i++)
                {
                    var type = _infos[i].TypeSrc;
                    _isNAs[i] = GetIsNADelegate(type);
                }
            }

            /// <summary>
            /// Returns the isNA predicate for the respective type.
            /// </summary>
            private Delegate GetIsNADelegate(ColumnType type)
            {
                Func<ColumnType, Delegate> func = GetIsNADelegate<int>;
                return Utils.MarshalInvoke(func, type.ItemType.RawType, type);
            }

            private Delegate GetIsNADelegate<T>(ColumnType type)
            {
                return Conversions.Instance.GetIsNAPredicate<T>(type.ItemType);
            }

            private ColInfo[] CreateInfos(ISchema inputSchema)
            {
                Host.AssertValue(inputSchema);
                var infos = new ColInfo[_parent.ColumnPairs.Length];
                for (int i = 0; i < _parent.ColumnPairs.Length; i++)
                {
                    if (!inputSchema.TryGetColumnIndex(_parent.ColumnPairs[i].input, out int colSrc))
                        throw Host.ExceptSchemaMismatch(nameof(inputSchema), "input", _parent.ColumnPairs[i].input);
                    _parent.CheckInputColumn(inputSchema, i, colSrc);
                    var type = inputSchema.GetColumnType(colSrc);
                    infos[i] = new ColInfo(_parent.ColumnPairs[i].output, _parent.ColumnPairs[i].input, type);
                }
                return infos;
            }

            public override RowMapperColumnInfo[] GetOutputColumns()
            {
                var result = new RowMapperColumnInfo[_parent.ColumnPairs.Length];
                for (int i = 0; i < _parent.ColumnPairs.Length; i++)
                {
                    InputSchema.TryGetColumnIndex(_parent.ColumnPairs[i].input, out int colIndex);
                    Host.Assert(colIndex >= 0);
                    var colMetaInfo = new ColumnMetadataInfo(_parent.ColumnPairs[i].output);
                    foreach (var type in InputSchema.GetMetadataTypes(colIndex).Where(x => x.Key == MetadataUtils.Kinds.SlotNames || x.Key == MetadataUtils.Kinds.IsNormalized))
                        Utils.MarshalInvoke(AddMetaGetter<int>, type.Value.RawType, colMetaInfo, InputSchema, type.Key, type.Value, colIndex);
                    result[i] = new RowMapperColumnInfo(_parent.ColumnPairs[i].output, _parent._types[i], colMetaInfo);
                }
                return result;
            }

            private int AddMetaGetter<T>(ColumnMetadataInfo colMetaInfo, ISchema schema, string kind, ColumnType ct, int originalCol)
            {
                MetadataUtils.MetadataGetter<T> getter = (int col, ref T dst) =>
                {
                    // We don't care about 'col': this getter is specialized for a column 'originalCol',
                    // and 'col' in this case is the 'metadata kind index', not the column index.
                    schema.GetMetadata<T>(kind, originalCol, ref dst);
                };
                var info = new MetadataInfo<T>(ct, getter);
                colMetaInfo.Add(kind, info);
                return 0;
            }

            protected override Delegate MakeGetter(IRow input, int iinfo, out Action disposer)
            {
                Host.AssertValue(input);
                Host.Assert(0 <= iinfo && iinfo < _infos.Length);
                disposer = null;

                if (!_infos[iinfo].TypeSrc.IsVector)
                    return ComposeGetterOne(input, iinfo);
                return ComposeGetterVec(input, iinfo);
            }

            /// <summary>
            /// Getter generator for single valued inputs.
            /// </summary>
            private Delegate ComposeGetterOne(IRow input, int iinfo)
                => Utils.MarshalInvoke(ComposeGetterOne<int>, _infos[iinfo].TypeSrc.RawType, input, iinfo);

            /// <summary>
            ///  Replaces NA values for scalars.
            /// </summary>
            private Delegate ComposeGetterOne<T>(IRow input, int iinfo)
            {
                var getSrc = input.GetGetter<T>(ColMapNewToOld[iinfo]);
                var src = default(T);
                var isNA = (RefPredicate<T>)_isNAs[iinfo];
                Host.Assert(_parent._repValues[iinfo] is T);
                T rep = (T)_parent._repValues[iinfo];
                ValueGetter<T> getter;

                return getter =
                    (ref T dst) =>
                    {
                        getSrc(ref src);
                        dst = isNA(ref src) ? rep : src;
                    };
            }

            /// <summary>
            /// Getter generator for vector valued inputs.
            /// </summary>
            private Delegate ComposeGetterVec(IRow input, int iinfo)
                => Utils.MarshalInvoke(ComposeGetterVec<int>, _infos[iinfo].TypeSrc.ItemType.RawType, input, iinfo);

            /// <summary>
            ///  Replaces NA values for vectors.
            /// </summary>
            private Delegate ComposeGetterVec<T>(IRow input, int iinfo)
            {
                //IVAN: check you use corret iinfo or maybe it should be ColMapNewToOld[iinfo] everywhere in code
                var getSrc = input.GetGetter<VBuffer<T>>(ColMapNewToOld[iinfo]);
                var isNA = (RefPredicate<T>)_isNAs[iinfo];
                var isDefault = Conversions.Instance.GetIsDefaultPredicate<T>(_infos[iinfo].TypeSrc.ItemType);

                var src = default(VBuffer<T>);
                ValueGetter<VBuffer<T>> getter;

                if (_parent._repIsDefault[iinfo] == null)
                {
                    // One replacement value for all slots.
                    Host.Assert(_parent._repValues[iinfo] is T);
                    T rep = (T)_parent._repValues[iinfo];
                    bool repIsDefault = isDefault(ref rep);
                    return getter =
                        (ref VBuffer<T> dst) =>
                        {
                            getSrc(ref src);
                            FillValues(ref src, ref dst, isNA, rep, repIsDefault);
                        };
                }

                // Replacement values by slot.
                Host.Assert(_parent._repValues[iinfo] is T[]);
                // The replacement array.
                T[] repArray = (T[])_parent._repValues[iinfo];

                return getter =
                    (ref VBuffer<T> dst) =>
                    {
                        getSrc(ref src);
                        Host.Check(src.Length == repArray.Length);
                        FillValues(ref src, ref dst, isNA, repArray, _parent._repIsDefault[iinfo]);
                    };
            }

            /// <summary>
            ///  Fills values for vectors where there is one replacement value.
            /// </summary>
            private void FillValues<T>(ref VBuffer<T> src, ref VBuffer<T> dst, RefPredicate<T> isNA, T rep, bool repIsDefault)
            {
                Host.AssertValue(isNA);

                int srcSize = src.Length;
                int srcCount = src.Count;
                var srcValues = src.Values;
                Host.Assert(Utils.Size(srcValues) >= srcCount);
                var srcIndices = src.Indices;

                var dstValues = dst.Values;
                var dstIndices = dst.Indices;

                // If the values array is not large enough, allocate sufficient space.
                // Note: We can't set the max to srcSize as vectors can be of variable lengths.
                Utils.EnsureSize(ref dstValues, srcCount, keepOld: false);

                int iivDst = 0;
                if (src.IsDense)
                {
                    // The source vector is dense.
                    Host.Assert(srcSize == srcCount);

                    for (int ivSrc = 0; ivSrc < srcCount; ivSrc++)
                    {
                        var srcVal = srcValues[ivSrc];

                        // The output for dense inputs is always dense.
                        // Note: Theoretically, one could imagine a dataset with NA values that one wished to replace with
                        // the default value, resulting in more than half of the indices being the default value.
                        // In this case, changing the dst vector to be sparse would be more memory efficient -- the current decision
                        // is it is not worth handling this case at the expense of running checks that will almost always not be triggered.
                        dstValues[ivSrc] = isNA(ref srcVal) ? rep : srcVal;
                    }
                    iivDst = srcCount;
                }
                else
                {
                    // The source vector is sparse.
                    Host.Assert(Utils.Size(srcIndices) >= srcCount);
                    Host.Assert(srcCount < srcSize);

                    // Allocate more space if necessary.
                    // REVIEW: One thing that changing the code to simply ensure that there are srcCount indices in the arrays
                    // does is over-allocate space if the replacement value is the default value in a dataset with a
                    // signficiant amount of NA values -- is it worth handling allocation of memory for this case?
                    Utils.EnsureSize(ref dstIndices, srcCount, keepOld: false);

                    // Note: ivPrev is only used for asserts.
                    int ivPrev = -1;
                    for (int iivSrc = 0; iivSrc < srcCount; iivSrc++)
                    {
                        Host.Assert(iivDst <= iivSrc);
                        var srcVal = srcValues[iivSrc];
                        int iv = srcIndices[iivSrc];
                        Host.Assert(ivPrev < iv & iv < srcSize);
                        ivPrev = iv;

                        if (!isNA(ref srcVal))
                        {
                            dstValues[iivDst] = srcVal;
                            dstIndices[iivDst++] = iv;
                        }
                        else if (!repIsDefault)
                        {
                            // Allow for further sparsification.
                            dstValues[iivDst] = rep;
                            dstIndices[iivDst++] = iv;
                        }
                    }
                    Host.Assert(iivDst <= srcCount);
                }
                Host.Assert(0 <= iivDst);
                Host.Assert(repIsDefault || iivDst == srcCount);
                dst = new VBuffer<T>(srcSize, iivDst, dstValues, dstIndices);
            }

            /// <summary>
            ///  Fills values for vectors where there is slot-wise replacement values.
            /// </summary>
            private void FillValues<T>(ref VBuffer<T> src, ref VBuffer<T> dst, RefPredicate<T> isNA, T[] rep, BitArray repIsDefault)
            {
                Host.AssertValue(rep);
                Host.Assert(rep.Length == src.Length);
                Host.AssertValue(repIsDefault);
                Host.Assert(repIsDefault.Length == src.Length);
                Host.AssertValue(isNA);

                int srcSize = src.Length;
                int srcCount = src.Count;
                var srcValues = src.Values;
                Host.Assert(Utils.Size(srcValues) >= srcCount);
                var srcIndices = src.Indices;

                var dstValues = dst.Values;
                var dstIndices = dst.Indices;

                // If the values array is not large enough, allocate sufficient space.
                Utils.EnsureSize(ref dstValues, srcCount, srcSize, keepOld: false);

                int iivDst = 0;
                Host.Assert(Utils.Size(srcValues) >= srcCount);
                if (src.IsDense)
                {
                    // The source vector is dense.
                    Host.Assert(srcSize == srcCount);

                    for (int ivSrc = 0; ivSrc < srcCount; ivSrc++)
                    {
                        var srcVal = srcValues[ivSrc];

                        // The output for dense inputs is always dense.
                        // Note: Theoretically, one could imagine a dataset with NA values that one wished to replace with
                        // the default value, resulting in more than half of the indices being the default value.
                        // In this case, changing the dst vector to be sparse would be more memory efficient -- the current decision
                        // is it is not worth handling this case at the expense of running checks that will almost always not be triggered.
                        dstValues[ivSrc] = isNA(ref srcVal) ? rep[ivSrc] : srcVal;
                    }
                    iivDst = srcCount;
                }
                else
                {
                    // The source vector is sparse.
                    Host.Assert(Utils.Size(srcIndices) >= srcCount);
                    Host.Assert(srcCount < srcSize);

                    // Allocate more space if necessary.
                    // REVIEW: One thing that changing the code to simply ensure that there are srcCount indices in the arrays
                    // does is over-allocate space if the replacement value is the default value in a dataset with a
                    // signficiant amount of NA values -- is it worth handling allocation of memory for this case?
                    Utils.EnsureSize(ref dstIndices, srcCount, srcSize, keepOld: false);

                    // Note: ivPrev is only used for asserts.
                    int ivPrev = -1;
                    for (int iivSrc = 0; iivSrc < srcCount; iivSrc++)
                    {
                        Host.Assert(iivDst <= iivSrc);
                        var srcVal = srcValues[iivSrc];
                        int iv = srcIndices[iivSrc];
                        Host.Assert(ivPrev < iv & iv < srcSize);
                        ivPrev = iv;

                        if (!isNA(ref srcVal))
                        {
                            dstValues[iivDst] = srcVal;
                            dstIndices[iivDst++] = iv;
                        }
                        else if (!repIsDefault[iv])
                        {
                            // Allow for further sparsification.
                            dstValues[iivDst] = rep[iv];
                            dstIndices[iivDst++] = iv;
                        }
                    }
                    Host.Assert(iivDst <= srcCount);
                }
                Host.Assert(0 <= iivDst);
                dst = new VBuffer<T>(srcSize, iivDst, dstValues, dstIndices);
            }

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
                        ctx.AddIntermediateVariable(_parent._types[iinfo], info.Name)))
                    {
                        ctx.RemoveColumn(info.Name, true);
                    }
                }
            }

            private bool SaveAsOnnxCore(OnnxContext ctx, int iinfo, ColInfo info, string srcVariableName, string dstVariableName)
            {
                DataKind rawKind;
                var type = _infos[iinfo].TypeSrc;
                if (type.IsVector)
                    rawKind = type.AsVector.ItemType.RawKind;
                else if (type.IsKey)
                    rawKind = type.AsKey.RawKind;
                else
                    rawKind = type.RawKind;

                if (rawKind != DataKind.R4)
                    return false;

                string opType = "Imputer";
                var node = ctx.CreateNode(opType, srcVariableName, dstVariableName, ctx.GetNodeName(opType));
                node.AddAttribute("replaced_value_float", Single.NaN);

                if (!_infos[iinfo].TypeSrc.IsVector)
                    node.AddAttribute("imputed_value_floats", Enumerable.Repeat((float)_parent._repValues[iinfo], 1));
                else
                {
                    if (_parent._repIsDefault[iinfo] != null)
                        node.AddAttribute("imputed_value_floats", (float[])_parent._repValues[iinfo]);
                    else
                        node.AddAttribute("imputed_value_floats", Enumerable.Repeat((float)_parent._repValues[iinfo], 1));
                }

                return true;
            }
        }
    }

    public sealed class NAReplaceEstimator : IEstimator<NAReplaceTransform>
    {
        private readonly IHost _host;
        private readonly NAReplaceTransform.ColumnInfo[] _columns;

        public NAReplaceEstimator(IHostEnvironment env, string name, string source = null, NAReplaceTransform.ReplacementKind replacementKind = NAReplaceTransform.ReplacementKind.DefaultValue)
            : this(env, new NAReplaceTransform.ColumnInfo(source ?? name, name, replacementKind))
        {

        }
        public NAReplaceEstimator(IHostEnvironment env, params NAReplaceTransform.ColumnInfo[] columns)
        {
            Contracts.CheckValue(env, nameof(env));
            _host = env.Register(nameof(NAReplaceEstimator));
            _columns = columns;
        }

        public SchemaShape GetOutputSchema(SchemaShape inputSchema)
        {
            _host.CheckValue(inputSchema, nameof(inputSchema));
            var result = inputSchema.Columns.ToDictionary(x => x.Name);
            foreach (var colInfo in _columns)
            {
                if (!inputSchema.TryFindColumn(colInfo.Input, out var col))
                    throw _host.ExceptSchemaMismatch(nameof(inputSchema), "input", colInfo.Input);
                //IVAN Proper validation, not copypaste from term estimator
                if ((col.ItemType.ItemType.RawKind == default) || !(col.ItemType.IsVector || col.ItemType.IsPrimitive))
                    throw _host.ExceptSchemaMismatch(nameof(inputSchema), "input", colInfo.Input);
                var metadata = new List<SchemaShape.Column>();
                if (col.Metadata.TryFindColumn(MetadataUtils.Kinds.SlotNames, out var slotMeta))
                    metadata.Add(slotMeta);
                if (col.Metadata.TryFindColumn(MetadataUtils.Kinds.IsNormalized, out var normalized))
                    metadata.Add(normalized);
                var type = !col.ItemType.IsVector ? col.ItemType : new VectorType(col.ItemType.ItemType.AsPrimitive, col.ItemType.AsVector);
                result[colInfo.Output] = new SchemaShape.Column(colInfo.Output, col.Kind, type, false, new SchemaShape(metadata.ToArray()));
            }

            return new SchemaShape(result.Values);
        }

        public NAReplaceTransform Fit(IDataView input) => new NAReplaceTransform(_host, input, _columns);
    }
}
