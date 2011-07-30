using System;
using System.Collections.Generic;
using GeoAPI.Geometries;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Union;
using NUnit.Framework;

namespace NetTopologySuite.Tests.NUnit.Operation.Union
{
    /// <summary>
    /// Compares the results of CascadedPolygonUnion to Geometry.union()
    /// using shape similarity measures.
    /// </summary>
    /// <author>mbdavis</author>
    public class CascadedPolygonUnionTester
    {
        public static double MIN_SIMILARITY_MEAURE = 0.999999;

        static PrecisionModel pm = new PrecisionModel();
        static GeometryFactory fact = new GeometryFactory(pm, 0);
        static WKTReader wktRdr = new WKTReader(fact);

        GeometryFactory geomFact = new GeometryFactory();

        public CascadedPolygonUnionTester()
        {
        }

        // TODO: Need to uncomment once the NetTopologySuite.Algorithm.Match namespace and classes are migrated to NTS
        //public bool Test(IList<IGeometry> geoms, double minimumMeasure)
        //{
        //    Console.WriteLine("Computing Iterated union");
        //    IGeometry union1 = UnionIterated(geoms);
        //    Console.WriteLine("Computing Cascaded union");
        //    IGeometry union2 = UnionCascaded(geoms);

        //    Console.WriteLine("Testing similarity with min measure = " + minimumMeasure);

        //    //double areaMeasure = (new AreaSimilarityMeasure()).Measure(union1, union2);
        //    //double hausMeasure = (new HausdorffSimilarityMeasure()).Measure(union1, union2);
        //    //double overallMeasure = SimilarityMeasureCombiner.Combine(areaMeasure, hausMeasure);

        //    Console.WriteLine(
        //            "Area measure = " + areaMeasure
        //            + "   Hausdorff measure = " + hausMeasure
        //            + "    Overall = " + overallMeasure);

        //    return overallMeasure > minimumMeasure;
        //}

        /*
        private void OLDdoTest(String filename, double distanceTolerance)
        throws IOException, ParseException
        {
          WKTFileReader fileRdr = new WKTFileReader(filename, wktRdr);
          List geoms = fileRdr.read();

          System.out.println("Computing Iterated union");
          Geometry union1 = unionIterated(geoms);
          System.out.println("Computing Cascaded union");
          Geometry union2 = unionCascaded(geoms);

          System.out.println("Testing similarity with tolerance = " + distanceTolerance);
          bool isSameWithinTolerance =  SimilarityValidator.isSimilar(union1, union2, distanceTolerance);


          Assert.IsTrue(isSameWithinTolerance);
        }
      */
        public IGeometry UnionIterated(IList<IGeometry> geoms)
        {
            IGeometry unionAll = null;
            int count = 0;
            foreach (var geom in geoms)
            {
                if (unionAll == null)
                {
                    unionAll = (Geometry)geom.Clone();
                }
                else
                {
                    unionAll = unionAll.Union(geom);
                }

                count++;
                if (count % 100 == 0)
                {
                    Console.Write(".");
                    //        System.out.println("Adding geom #" + count);
                }
            }
            return unionAll;
        }

        public IGeometry UnionCascaded(IList<IGeometry> geoms)
        {
            return CascadedPolygonUnion.Union(geoms);
        }
    }
}
