// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Data;
using System;

namespace Microsoft.ML.StaticPipe
{
    public static class DataLoadSaveOperationsExtensions
    {
        /// <summary>
        /// Configures a reader for text files.
        /// </summary>
        /// <typeparam name="TShape">The type shape parameter, which must be a valid-schema shape. As a practical
        /// matter this is generally not explicitly defined from the user, but is instead inferred from the return
        /// type of the <paramref name="func"/> where one takes an input <see cref="TextLoader.Context"/> and uses it to compose
        /// a shape-type instance describing what the columns are and how to load them from the file.</typeparam>
        /// <param name="catalog">The catalog.</param>
        /// <param name="func">The delegate that describes what fields to read from the text file, as well as
        /// describing their input type. The way in which it works is that the delegate is fed a <see cref="TextLoader.Context"/>,
        /// and the user composes a shape type with <see cref="PipelineColumn"/> instances out of that <see cref="TextLoader.Context"/>.
        /// The resulting data will have columns with the names corresponding to their names in the shape type.</param>
        /// <param name="files">Input files.</param>
        /// <param name="hasHeader">Data file has header with feature names.</param>
        /// <param name="separator">Text field separator.</param>
        /// <param name="allowQuoting">Whether the input -may include quoted values, which can contain separator
        /// characters, colons, and distinguish empty values from missing values. When true, consecutive separators
        /// denote a missing value and an empty value is denoted by <c>""</c>. When false, consecutive separators
        /// denote an empty value.</param>
        /// <param name="allowSparse">Whether the input may include sparse representations.</param>
        /// <param name="trimWhitspace">Remove trailing whitespace from lines.</param>
        /// <returns>A configured statically-typed reader for text files.</returns>
        public static DataReader<IMultiStreamSource, TShape> TextReader<[IsShape] TShape>(
            this DataOperations catalog, Func<TextLoader.Context, TShape> func, IMultiStreamSource files = null,
            bool hasHeader = false, char separator = '\t', bool allowQuoting = true, bool allowSparse = true,
            bool trimWhitspace = false)
         => TextLoader.CreateReader(catalog.Environment, func, files, hasHeader, separator, allowQuoting, allowSparse, trimWhitspace);

        /// <summary>
        /// Read data from provided path(s).
        /// </summary>
        /// <typeparam name="TShape">The type shape parameter, which must be a valid-schema shape.</typeparam>
        /// <param name="reader">Configured statically typed reader.</param>
        /// <param name="path">Path to text file(s) to read data from.</param>
        /// <returns>Dataview parameterized by <typeparamref name="TShape"/>.</returns>
        public static DataView<TShape> Read<TShape>(this DataReader<IMultiStreamSource, TShape> reader, params string[] path)
        {
            return reader.Read(new MultiFileSource(path));
        }
    }
}
