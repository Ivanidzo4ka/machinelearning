﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Transforms.Projections;

namespace Microsoft.ML
{
    public static class ProjectionCatalog
    {
        /// <summary>
        /// Initializes a new instance of <see cref="RandomFourierFeaturizingEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="inputColumn">Name of the column to be transformed.</param>
        /// <param name="outputColumn">Name of the output column. If this is null '<paramref name="inputColumn"/>' will be used.</param>
        /// <param name="newDim">The number of random Fourier features to create.</param>
        /// <param name="useSin">Create two features for every random Fourier frequency? (one for cos and one for sin).</param>
        public static RandomFourierFeaturizingEstimator CreateRandomFourierFeatures(this TransformsCatalog.ProjectionTransforms catalog,
            string inputColumn,
            string outputColumn = null,
            int newDim = RandomFourierFeaturizingEstimator.Defaults.NewDim,
            bool useSin = RandomFourierFeaturizingEstimator.Defaults.UseSin)
            => new RandomFourierFeaturizingEstimator(CatalogUtils.GetEnvironment(catalog), inputColumn, outputColumn, newDim, useSin);

        /// <summary>
        /// Initializes a new instance of <see cref="RandomFourierFeaturizingEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="columns">The input columns to use for the transformation.</param>
        public static RandomFourierFeaturizingEstimator CreateRandomFourierFeatures(this TransformsCatalog.ProjectionTransforms catalog, params RandomFourierFeaturizingTransformer.ColumnInfo[] columns)
            => new RandomFourierFeaturizingEstimator(CatalogUtils.GetEnvironment(catalog), columns);

        /// <summary>
        /// Initializes a new instance of <see cref="VectorWhiteningEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="inputColumn">Name of the input column.</param>
        /// <param name="outputColumn">Name of the column resulting from the transformation of <paramref name="inputColumn"/>. Null means <paramref name="inputColumn"/> is replaced. </param>
        /// <param name="kind">Whitening kind (PCA/ZCA).</param>
        /// <param name="eps">Whitening constant, prevents division by zero.</param>
        /// <param name="maxRows">Maximum number of rows used to train the transform.</param>
        /// <param name="pcaNum">In case of PCA whitening, indicates the number of components to retain.</param>
        public static VectorWhiteningEstimator VectorWhiten(this TransformsCatalog.ProjectionTransforms catalog, string inputColumn, string outputColumn,
            WhiteningKind kind = VectorWhiteningTransformer.Defaults.Kind,
            float eps = VectorWhiteningTransformer.Defaults.Eps,
            int maxRows = VectorWhiteningTransformer.Defaults.MaxRows,
            int pcaNum = VectorWhiteningTransformer.Defaults.PcaNum)
            => new VectorWhiteningEstimator(CatalogUtils.GetEnvironment(catalog), inputColumn, outputColumn, kind, eps, maxRows, pcaNum);

        /// <summary>
        /// Initializes a new instance of <see cref="VectorWhiteningEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="columns"> Describes the parameters of the whitening process for each column pair.</param>
        public static VectorWhiteningEstimator VectorWhiten(this TransformsCatalog.ProjectionTransforms catalog, params VectorWhiteningTransformer.ColumnInfo[] columns)
            => new VectorWhiteningEstimator(CatalogUtils.GetEnvironment(catalog), columns);

        /// <summary>
        /// Initializes a new instance of <see cref="LpNormalizingEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="inputColumn">Name of the input column.</param>
        /// <param name="outputColumn">Name of the column resulting from the transformation of <paramref name="inputColumn"/>. Null means <paramref name="inputColumn"/> is replaced. </param>
        /// <param name="normKind">Type of norm to use to normalize each sample.</param>
        /// <param name="subMean">Subtract mean from each value before normalizing.</param>
        public static LpNormalizingEstimator LpNormalize(this TransformsCatalog.ProjectionTransforms catalog, string inputColumn, string outputColumn,
            LpNormalizingEstimatorBase.NormalizerKind normKind = LpNormalizingEstimatorBase.Defaults.NormKind, bool subMean = LpNormalizingEstimatorBase.Defaults.LpSubMean)
            => new LpNormalizingEstimator(CatalogUtils.GetEnvironment(catalog), inputColumn, outputColumn, normKind, subMean);

        /// <summary>
        /// Initializes a new instance of <see cref="LpNormalizingEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="columns"> Describes the parameters of the lp-normalization process for each column pair.</param>
        public static LpNormalizingEstimator LpNormalize(this TransformsCatalog.ProjectionTransforms catalog, params LpNormalizingTransformer.LpNormColumnInfo[] columns)
            => new LpNormalizingEstimator(CatalogUtils.GetEnvironment(catalog), columns);

        /// <summary>
        /// Initializes a new instance of <see cref="GcnNormalizingEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="inputColumn">Name of the input column.</param>
        /// <param name="outputColumn">Name of the column resulting from the transformation of <paramref name="inputColumn"/>. Null means <paramref name="inputColumn"/> is replaced. </param>
        /// <param name="subMean">Subtract mean from each value before normalizing.</param>
        /// <param name="useStdDev">Normalize by standard deviation rather than L2 norm.</param>
        /// <param name="scale">Scale features by this value.</param>
        public static GcnNormalizingEstimator GcnNormalize(this TransformsCatalog.ProjectionTransforms catalog, string inputColumn, string outputColumn,
             bool subMean = LpNormalizingEstimatorBase.Defaults.GcnSubMean,
             bool useStdDev = LpNormalizingEstimatorBase.Defaults.UseStdDev,
             float scale = LpNormalizingEstimatorBase.Defaults.Scale)
            => new GcnNormalizingEstimator(CatalogUtils.GetEnvironment(catalog), inputColumn, outputColumn, subMean, useStdDev, scale);

        /// <summary>
        /// Initializes a new instance of <see cref="VectorWhiteningEstimator"/>.
        /// </summary>
        /// <param name="catalog">The transform's catalog.</param>
        /// <param name="columns"> Describes the parameters of the gcn-normaliztion process for each column pair.</param>
        public static GcnNormalizingEstimator GcnNormalize(this TransformsCatalog.ProjectionTransforms catalog, params LpNormalizingTransformer.GcnColumnInfo[] columns)
            => new GcnNormalizingEstimator(CatalogUtils.GetEnvironment(catalog), columns);
    }
}
