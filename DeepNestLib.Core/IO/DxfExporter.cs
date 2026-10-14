namespace DeepNestLib.IO
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using DeepNestLib.Geometry;
  using DeepNestLib.Placement;
  using IxMilia.Dxf;
  using IxMilia.Dxf.Entities;

  public class DxfExporter : ExporterBase, IExport
  {
    public const string CutInnerLayer = "01_CUT_INNER";
    public const string CommonLineLayer = "02_COMMON_LINE";
    public const string CutOuterLayer = "03_CUT_OUTER";
    public const string OffcutLayer = "04_OFFCUT";

    public DxfPartNameExport PartNameExport { get; set; } = DxfPartNameExport.None;

    /// <summary>
    /// Keep the layers the geometry is built on (<see cref="CutInnerLayer"/>, <see cref="CommonLineLayer"/>,
    /// <see cref="CutOuterLayer"/>, <see cref="OffcutLayer"/>, the source drawing's own) instead of writing
    /// everything cut onto <see cref="DxfOutputLayers.Cut"/>. For tests that check what each line is for; a
    /// file for the laser never wants it.
    /// </summary>
    public bool RoleLayers { get; set; }

    public override string SaveFileDialogFilter => "Dxf files (*.dxf)|*.dxf";

    public override async Task Export(
      string path,
      ISheetPlacement sheetPlacement,
      bool doMergeLines,
      IReadOnlyCollection<OffcutLine> offcutLines = null)
    {
      if (sheetPlacement.CommonEdges == null || sheetPlacement.CommonEdges.Count == 0)
      {
        await base.Export(path, sheetPlacement, doMergeLines, offcutLines).ConfigureAwait(false);
        return;
      }

      DxfFile dxf = this.Finish(GenerateCommonLineDxfFile(sheetPlacement, offcutLines, this.PartNameExport));
      if (dxf.Entities.Count == 0)
      {
        return;
      }

      await Task.Run(() => dxf.Save(path, true)).ConfigureAwait(false);
    }

    public async Task Export(Stream stream, ISheetPlacement sheetPlacement, bool doMergeLines)
    {
      if (sheetPlacement.CommonEdges != null && sheetPlacement.CommonEdges.Count > 0)
      {
        DxfFile dxf = this.Finish(GenerateCommonLineDxfFile(sheetPlacement, null, this.PartNameExport));
        if (dxf.Entities.Count > 0)
        {
          await Task.Run(() => dxf.Save(stream, true)).ConfigureAwait(false);
        }

        return;
      }

      await Export(stream, sheetPlacement.PolygonsForExport, sheetPlacement.Sheet, doMergeLines);
    }

    public async Task Export(Stream stream, IEnumerable<INfp> polygons, ISheet sheet, bool doMergeLines)
    {
      DxfFile sheetdxf = this.Finish(GenerateDxfFile(polygons, sheet, sheet.Id, doMergeLines, null, this.PartNameExport));
      var dxf = sheetdxf;
      var id = sheet.Id;

      // (Guard was `!= 1` when the sheet outline was always entity #0 — now any entity means content.)
      if (dxf.Entities.Count > 0)
      {
        await Task.Run(() =>
        {
          dxf.Save(stream, true);
          //dxf.Save(@"C:\Temp\DeepnestSharp.dxf");
        }).ConfigureAwait(false);
      }
    }

    protected async override Task Export(string path, IEnumerable<INfp> polygons, IEnumerable<ISheet> sheets, bool doMergeLines, IReadOnlyCollection<OffcutLine> offcutLines = null)
    {
      try
      {
        Dictionary<DxfFile, int> dxfexports = new Dictionary<DxfFile, int>();
        var sheetList = sheets.ToList();
        for (var i = 0; i < sheets.Count(); i++)
        {
          var sheet = sheetList[i];
          DxfFile sheetdxf = this.Finish(GenerateDxfFile(polygons, sheet, i, doMergeLines, offcutLines, this.PartNameExport));
          dxfexports.Add(sheetdxf, sheet.Id);
        }

        for (var i = 0; i < dxfexports.Count(); i++)
        {
          var dxf = dxfexports.ElementAt(i).Key;
          var id = dxfexports.ElementAt(i).Value;

          if (dxf.Entities.Count > 0)
          {
            FileInfo fi = new FileInfo(path);
            await Task.Run(() =>
            {
              if (dxfexports.Count() == 1)
              {
                dxf.Save(fi.FullName, true);
              }
              else
              {
                dxf.Save($"{fi.FullName.Substring(0, fi.FullName.Length - 4)}{id}.dxf", true);
              }
            }).ConfigureAwait(false);
          }
        }
      }
      catch (Exception ex)
      {
        throw;
      }
    }

    private static DxfFile GenerateEmptyDxfFile()
    {
      // PARTS ONLY — no sheet outline: the plate rectangle used to be written as a first entity, but
      // downstream CAM treats every entity as geometry to cut, and reimporting such a file looks like
      // one giant nested-sheet part. The sheet size lives in the CAM/job setup, not in the cut file.
      var sheetdxf = new DxfFile();

      // Write DXF R2000 (AC1015) instead of the default R12: R12 symbol/table names can't contain
      // spaces, so a layer like "Plate H130 W70" makes AutoCAD reject the whole file. R2000 allows
      // spaces in names and preserves LWPOLYLINE/arcs more faithfully. Universally supported.
      sheetdxf.Header.Version = DxfAcadVersion.R2000;
      sheetdxf.Views.Clear();

      return sheetdxf;
    }

    /// <summary>
    /// Puts a finished sheet on the shop's layers. The generators leave the cut on its role layers (or the
    /// source drawing's own layer, in whatever colour it was drawn), bends and engraved names on
    /// <see cref="DxfOutputLayers.Paint"/> and display names on <see cref="DxfOutputLayers.Background"/>;
    /// anything that is not on one of the last two is cut. Every entity is written ByLayer, cut first, so a
    /// CAM that sorts by colour, by layer or by the order they come in sees the same three.
    /// </summary>
    private DxfFile Finish(DxfFile dxf)
    {
      if (this.RoleLayers || dxf == null)
      {
        return dxf;
      }

      string Role(DxfEntity e) => e.Layer == DxfOutputLayers.Paint.Name || e.Layer == DxfOutputLayers.Background.Name
        ? e.Layer
        : DxfOutputLayers.Cut.Name;

      var ordered = dxf.Entities
        .Select((e, index) => (Entity: e, Index: index, Rank: Role(e) == DxfOutputLayers.Cut.Name ? 0 : Role(e) == DxfOutputLayers.Paint.Name ? 1 : 2))
        .OrderBy(t => t.Rank)
        .ThenBy(t => t.Index)
        .Select(t => t.Entity)
        .ToList();
      dxf.Entities.Clear();
      foreach (var entity in ordered)
      {
        entity.Layer = Role(entity);
        entity.Color = DxfColor.ByLayer;
        entity.LineTypeName = "BYLAYER";
        dxf.Entities.Add(entity);
      }

      // The colour is the shop's; a line type the drawing gave the layer (bend lines are PHANTOM) is kept, it
      // only changes how the line looks in CAD.
      var lineTypes = dxf.Layers
        .Where(l => !string.IsNullOrWhiteSpace(l.LineTypeName) && dxf.LineTypes.Any(t => string.Equals(t.Name, l.LineTypeName, StringComparison.OrdinalIgnoreCase)))
        .GroupBy(l => l.Name)
        .ToDictionary(g => g.Key, g => g.First().LineTypeName);
      dxf.Layers.Clear();
      foreach (var layer in new[] { DxfOutputLayers.Cut, DxfOutputLayers.Paint, DxfOutputLayers.Background })
      {
        if (layer == DxfOutputLayers.Cut || ordered.Any(e => e.Layer == layer.Name))
        {
          dxf.Layers.Add(new DxfLayer(layer.Name, DxfColor.FromIndex((byte)layer.ColorIndex))
          {
            LineTypeName = lineTypes.TryGetValue(layer.Name, out string lineType) ? lineType : "CONTINUOUS",
            IsLayerOn = true,
          });
        }
      }

      if (!dxf.Layers.Any(l => l.Name == "0"))
      {
        dxf.Layers.Add(new DxfLayer("0", DxfColor.FromIndex(7)) { LineTypeName = "CONTINUOUS", IsLayerOn = true });
      }

      return dxf;
    }

    private static IEnumerable<DxfEntity> GetOffsetDxfEntities(IEnumerable<INfp> polygons, ISheet sheet, int i)
    {
      foreach (var polygon in polygons)
      {
        RawDetail<DxfEntity> fl;
        if (polygon.Fitted == false || !DxfFileExtensions.IsDxf(polygon.Name) || polygon.Sheet.Id != sheet.Id)
        {
          continue;
        }
        else
        {
          try
          {
#if NCRUNCH
            if (polygon.IsExact && !(new FileInfo(polygon.Name).Exists))
            {
              fl = DxfParser.ConvertDxfToRawDetail("generated.dxf", polygon.ToDxfFile().Entities.ToArray());
            }
            else
#endif
            {
              fl = DxfParser.ConvertDxfToRawDetail(polygon.Name, DrawingFile.Load(polygon.Name).Entities.ToArray());
            }
          }
          catch
          {
            throw new FileNotFoundException($"The file {polygon.Name} could not be loaded. When exporting the original files that generated the nest are used for precision, instead of the copies rotated and manipulated potentially many times during the nest; degrading potentially their accuracy. It would be possible to load and store the original files but that'd take some effort...");
          }
        }

        var sheetXoffset = -sheet.WidthCalculated * i;
        //double sheetYoffset = -sheet.Height * i;
        DxfPoint offsetdistance = new DxfPoint(polygon.X + sheetXoffset, polygon.Y, 0D);
        List<DxfEntity> newlist = OffsetToNest(fl.Outers, offsetdistance, polygon.Rotation, polygon.IsMirrored);
        foreach (DxfEntity ent in newlist)
        {
          yield return ent;
        }
      }
    }

    private static List<DxfEntity> OffsetToNest(IEnumerable<ILocalContour> contours, DxfPoint offsetdistance, double rotation, bool mirror)
    {
      var allEntities = new List<DxfEntity>();
      foreach (var contour in contours)
      {
        if (contour is LocalContour<DxfEntity> castContour)
        {
          allEntities.AddRange(castContour.Entities);
        }
      }

      return OffsetToNest(allEntities, offsetdistance, rotation, mirror);
    }

    /// <summary>Moves a part's own entities to where a placement puts them, as the export does. The entities
    /// are changed in place.</summary>
    public static List<DxfEntity> PlaceEntities(IList<DxfEntity> entities, DxfPoint offset, double rotation, bool mirror)
      => OffsetToNest(entities, offset, rotation, mirror);

    private static List<DxfEntity> OffsetToNest(IList<DxfEntity> dxfEntities, DxfPoint offset, double rotationAngle, bool mirror)
    {
      var result = new List<DxfEntity>();
      List<DxfPoint> tmpPts;
      foreach (DxfEntity entity in dxfEntities)
      {
        switch (entity.EntityType)
        {
          case DxfEntityType.Arc:
            DxfArc dxfArc = (DxfArc)entity;
            dxfArc.Center = TransformLocation(mirror, rotationAngle, dxfArc.Center);
            dxfArc.Center += offset;
            if (mirror)
            {
              // Reflection across Y reverses the arc's CCW sweep: swap the endpoints and map θ -> 180-θ.
              double s = dxfArc.StartAngle, en = dxfArc.EndAngle;
              dxfArc.StartAngle = 180.0 - en + rotationAngle;
              dxfArc.EndAngle = 180.0 - s + rotationAngle;
            }
            else
            {
              dxfArc.StartAngle += rotationAngle;
              dxfArc.EndAngle += rotationAngle;
            }

            result.Add(dxfArc);
            break;

          case DxfEntityType.ArcAlignedText:
            DxfArcAlignedText dxfArcAligned = (DxfArcAlignedText)entity;
            dxfArcAligned.CenterPoint = TransformLocation(mirror, rotationAngle, dxfArcAligned.CenterPoint);
            dxfArcAligned.CenterPoint += offset;
            if (mirror)
            {
              double s = dxfArcAligned.StartAngle, en = dxfArcAligned.EndAngle;
              dxfArcAligned.StartAngle = 180.0 - en + rotationAngle;
              dxfArcAligned.EndAngle = 180.0 - s + rotationAngle;
            }
            else
            {
              dxfArcAligned.StartAngle += rotationAngle;
              dxfArcAligned.EndAngle += rotationAngle;
            }

            result.Add(dxfArcAligned);
            break;

          case DxfEntityType.Attribute:
            DxfAttribute dxfAttribute = (DxfAttribute)entity;
            dxfAttribute.Location = TransformLocation(mirror, rotationAngle, dxfAttribute.Location);
            dxfAttribute.Location += offset;
            result.Add(dxfAttribute);
            break;

          case DxfEntityType.AttributeDefinition:
            DxfAttributeDefinition dxfAttributecommon = (DxfAttributeDefinition)entity;
            dxfAttributecommon.Location = TransformLocation(mirror, rotationAngle, dxfAttributecommon.Location);
            dxfAttributecommon.Location += offset;
            result.Add(dxfAttributecommon);
            break;

          case DxfEntityType.Circle:
            DxfCircle dxfCircle = (DxfCircle)entity;
            dxfCircle.Center = TransformLocation(mirror, rotationAngle, dxfCircle.Center);
            dxfCircle.Center += offset;
            result.Add(dxfCircle);
            break;

          case DxfEntityType.Ellipse:
            DxfEllipse dxfEllipse = (DxfEllipse)entity;
            dxfEllipse.Center = TransformLocation(mirror, rotationAngle, dxfEllipse.Center);
            dxfEllipse.Center += offset;
            DxfVector majorAxis = dxfEllipse.MajorAxis;
            DxfPoint transformedMajorAxis = TransformLocation(
              mirror,
              rotationAngle,
              new DxfPoint(majorAxis.X, majorAxis.Y, majorAxis.Z));
            dxfEllipse.MajorAxis = new DxfVector(
              transformedMajorAxis.X,
              transformedMajorAxis.Y,
              transformedMajorAxis.Z);
            if (mirror && Math.Abs(dxfEllipse.EndParameter - dxfEllipse.StartParameter - (2 * Math.PI)) > 1e-9)
            {
              // The minor axis follows the major one anticlockwise, so a reflected ellipse runs its parameter
              // the other way: t becomes -t and the ends swap, or a mirrored part cuts the other half.
              double s = dxfEllipse.StartParameter, en = dxfEllipse.EndParameter;
              dxfEllipse.StartParameter = ((-en % (2 * Math.PI)) + (2 * Math.PI)) % (2 * Math.PI);
              dxfEllipse.EndParameter = ((-s % (2 * Math.PI)) + (2 * Math.PI)) % (2 * Math.PI);
            }

            result.Add(dxfEllipse);
            break;

          case DxfEntityType.Image:
            DxfImage dxfImage = (DxfImage)entity;
            dxfImage.Location = TransformLocation(mirror, rotationAngle, dxfImage.Location);
            dxfImage.Location += offset;

            result.Add(dxfImage);
            break;

          case DxfEntityType.Leader:
            DxfLeader dxfLeader = (DxfLeader)entity;
            tmpPts = new List<DxfPoint>();

            foreach (DxfPoint vrt in dxfLeader.Vertices)
            {
              var tmppnt = TransformLocation(mirror, rotationAngle, vrt);
              tmppnt += offset;
              tmpPts.Add(tmppnt);
            }

            dxfLeader.Vertices.Clear();
            dxfLeader.Vertices.Concat(tmpPts);
            result.Add(dxfLeader);
            break;

          case DxfEntityType.Line:
            DxfLine dxfLine = (DxfLine)entity;
            dxfLine.P1 = TransformLocation(mirror, rotationAngle, dxfLine.P1);
            dxfLine.P2 = TransformLocation(mirror, rotationAngle, dxfLine.P2);
            dxfLine.P1 += offset;
            dxfLine.P2 += offset;
            result.Add(dxfLine);
            break;

          case DxfEntityType.LwPolyline:
            // A LwPolyline's entity type is DxfLwPolyline (NOT DxfPolyline); its vertices are
            // DxfLwPolylineVertex (X/Y/Bulge), not DxfVertex. The old (DxfPolyline) cast always threw.
            DxfLwPolyline dxfLwPoly = (DxfLwPolyline)entity;
            foreach (DxfLwPolylineVertex vrt in dxfLwPoly.Vertices)
            {
              var rotated = TransformLocation(mirror, rotationAngle, new DxfPoint(vrt.X, vrt.Y, 0));
              rotated += offset;
              vrt.X = rotated.X;
              vrt.Y = rotated.Y;
              if (mirror)
              {
                vrt.Bulge = -vrt.Bulge; // reflection flips the sign of the arc bulge
              }
            }

            result.Add(dxfLwPoly);
            break;

          case DxfEntityType.MLine:
            DxfMLine mLine = (DxfMLine)entity;
            tmpPts = new List<DxfPoint>();
            mLine.StartPoint += offset;

            mLine.StartPoint = TransformLocation(mirror, rotationAngle, mLine.StartPoint);

            foreach (DxfPoint vrt in mLine.Vertices)
            {
              var tmppnt = TransformLocation(mirror, rotationAngle, vrt);
              tmppnt += offset;
              tmpPts.Add(tmppnt);
            }

            mLine.Vertices.Clear();
            mLine.Vertices.Concat(tmpPts);
            result.Add(mLine);
            break;

          case DxfEntityType.Polyline:
            DxfPolyline polyline = (DxfPolyline)entity;

            List<DxfVertex> verts = new List<DxfVertex>();
            foreach (DxfVertex vrt in polyline.Vertices)
            {
              var tmppnt = vrt;
              tmppnt.Location = TransformLocation(mirror, rotationAngle, tmppnt.Location);
              tmppnt.Location += offset;
              if (mirror)
              {
                tmppnt.Bulge = -tmppnt.Bulge;
              }

              verts.Add(tmppnt);
            }

            DxfPolyline polyout = new DxfPolyline(verts);
            polyout.Location = polyline.Location + offset;
            polyout.IsClosed = polyline.IsClosed;
            polyout.Layer = polyline.Layer;
            result.Add(polyout);

            break;

          case DxfEntityType.Spline:
            {
              // An affine map of a NURBS is a NURBS with mapped control points, so the knots and weights
              // ride along untouched. Mirroring reflects the control points; the curve follows.
              DxfSpline dxfSpline = (DxfSpline)entity;
              Replace(dxfSpline.ControlPoints, dxfSpline.ControlPoints
                .Select(cp => new DxfControlPoint(TransformLocation(mirror, rotationAngle, cp.Point) + offset, cp.Weight)).ToList());
              Replace(dxfSpline.FitPoints, dxfSpline.FitPoints
                .Select(p => TransformLocation(mirror, rotationAngle, p) + offset).ToList());
              result.Add(dxfSpline);
              break;
            }

          case DxfEntityType.Body:
          case DxfEntityType.DgnUnderlay:
          case DxfEntityType.Dimension:
          case DxfEntityType.DwfUnderlay:
          case DxfEntityType.Face:
          case DxfEntityType.Helix:
          case DxfEntityType.Insert:
          case DxfEntityType.Light:
          case DxfEntityType.ModelerGeometry:
          case DxfEntityType.MText:
          case DxfEntityType.OleFrame:
          case DxfEntityType.Ole2Frame:
          case DxfEntityType.PdfUnderlay:
          case DxfEntityType.Point:
          case DxfEntityType.ProxyEntity:
          case DxfEntityType.Ray:
          case DxfEntityType.Region:
          case DxfEntityType.RText:
          case DxfEntityType.Section:
          case DxfEntityType.Seqend:
          case DxfEntityType.Shape:
          case DxfEntityType.Solid:
          case DxfEntityType.Text:
          case DxfEntityType.Tolerance:
          case DxfEntityType.Trace:
          case DxfEntityType.Underlay:
          case DxfEntityType.Vertex:
          case DxfEntityType.WipeOut:
          case DxfEntityType.XLine:
            throw new ArgumentException("unsupported entity type: " + entity.EntityType);
        }
      }

      return result;
    }

    private static DxfPoint RotateLocation(double rotationAngle, DxfPoint pt)
    {
      var angle = GeometryUtil.ToRadians(rotationAngle);
      var x = pt.X;
      var y = pt.Y;
      var x1 = (double)(x * Math.Cos(angle) - y * Math.Sin(angle));
      var y1 = (double)(x * Math.Sin(angle) + y * Math.Cos(angle));
      return new DxfPoint(x1, y1, pt.Z);
    }

    /// <summary>Applies the placement's transform to a point in the original file's frame: mirror across Y
    /// (negate X) first when the placement is mirrored, THEN rotate — matching how the on-screen/nested
    /// geometry was built (mirror-then-rotate), so the exported part lands exactly where it's shown.</summary>
    private static DxfPoint TransformLocation(bool mirror, double rotationAngle, DxfPoint pt)
    {
      if (mirror)
      {
        pt = new DxfPoint(-pt.X, pt.Y, pt.Z);
      }

      return RotateLocation(rotationAngle, pt);
    }

    /// <summary>Swaps the contents of a list in place; the entities own their collections.</summary>
    private static void Replace<T>(IList<T> target, IList<T> replacement)
    {
      target.Clear();
      foreach (T item in replacement)
      {
        target.Add(item);
      }
    }

    /// <summary>Rounds a coordinate to the export grid (micro-inch).</summary>
    internal static double Snap(double v) => Math.Round(v, 6);

    /// <summary>
    /// Snaps every entity's coordinates to the micro-inch grid (in place). Positions only — bulges
    /// are dimensionless ratios and stay untouched. Unknown entity types pass through unchanged.
    /// </summary>
    internal static IEnumerable<DxfEntity> SnapToGrid(IEnumerable<DxfEntity> entities)
    {
      foreach (var entity in entities)
      {
        switch (entity)
        {
          case DxfLine line:
            line.P1 = new DxfPoint(Snap(line.P1.X), Snap(line.P1.Y), Snap(line.P1.Z));
            line.P2 = new DxfPoint(Snap(line.P2.X), Snap(line.P2.Y), Snap(line.P2.Z));
            break;
          case DxfArc arc:
            arc.Center = new DxfPoint(Snap(arc.Center.X), Snap(arc.Center.Y), Snap(arc.Center.Z));
            arc.Radius = Snap(arc.Radius);
            arc.StartAngle = Snap(arc.StartAngle);
            arc.EndAngle = Snap(arc.EndAngle);
            break;
          case DxfCircle circle:
            circle.Center = new DxfPoint(Snap(circle.Center.X), Snap(circle.Center.Y), Snap(circle.Center.Z));
            circle.Radius = Snap(circle.Radius);
            break;
          case DxfLwPolyline lw:
            foreach (var v in lw.Vertices)
            {
              v.X = Snap(v.X);
              v.Y = Snap(v.Y);
            }

            break;
          case DxfPolyline poly:
            foreach (var v in poly.Vertices)
            {
              v.Location = new DxfPoint(Snap(v.Location.X), Snap(v.Location.Y), Snap(v.Location.Z));
            }

            break;
        }

        yield return entity;
      }
    }

    private static DxfFile GenerateCommonLineDxfFile(
      ISheetPlacement sheetPlacement,
      IReadOnlyCollection<OffcutLine> offcutLines = null,
      DxfPartNameExport partNameExport = DxfPartNameExport.None)
    {
      IReadOnlyDictionary<string, DxfSourceSnapshot> sourceSnapshots
        = CaptureSourceSnapshots(
          sheetPlacement.PartPlacements,
          sheetPlacement.GeometryTolerance);
      CommonLineGeometryResult geometry = CommonLineGeometryResolver.Resolve(
        sheetPlacement,
        sourceSnapshots.ToDictionary(
          pair => pair.Key,
          pair => pair.Value.Catalog,
          StringComparer.OrdinalIgnoreCase));
      var clusteredPartIds = new HashSet<int>(geometry.Clusters.SelectMany(cluster => cluster.PartIds));
      DxfFile dxf = GenerateEmptyDxfFile();
      AddCommonLineLayers(dxf);

      // Preserve original DXF primitives for ordinary parts and all holes. Only the outside contour of
      // a cluster member is suppressed; it is replaced by the one closed cluster boundary below.
      foreach (IPartPlacement placement in sheetPlacement.PartPlacements)
      {
        bool clustered = clusteredPartIds.Contains(placement.Id);
        foreach ((bool IsChild, List<DxfEntity> Entities) contour in LoadPlacedContours(
          placement,
          sourceSnapshots))
        {
          if (clustered && !contour.IsChild)
          {
            EnsureClusterOuterSourceIsSupported(contour.Entities, placement.Id);
            continue;
          }

          string layer = contour.IsChild ? CutInnerLayer : CutOuterLayer;
          List<DxfEntity> closed = EnsureClosedContour(
            contour.Entities,
            layer,
            sheetPlacement.GeometryTolerance);
          foreach (DxfEntity entity in closed)
          {
            dxf.Entities.Add(entity);
          }
        }
      }

      foreach (CommonLineCluster cluster in geometry.Clusters)
      {
        foreach (CommonLinePath boundary in cluster.InnerBoundaries)
        {
          if (!boundary.IsClosed || boundary.Points.Count < 3)
          {
            throw new CommonLineValidationException("CUT_INNER contains an open or degenerate cluster contour.");
          }

          var polyline = new DxfLwPolyline(boundary.Points.Select((point, index) => new DxfLwPolylineVertex
          {
            X = Snap(point.X),
            Y = Snap(point.Y),
            Bulge = boundary.Bulges[index],
          }))
          {
            IsClosed = true,
            Layer = CutInnerLayer,
          };
          dxf.Entities.Add(polyline);
        }

        foreach (CommonLinePath boundary in cluster.OuterBoundaries)
        {
          if (!boundary.IsClosed || boundary.Points.Count < 3)
          {
            throw new CommonLineValidationException("CUT_OUTER contains an open or degenerate cluster contour.");
          }

          var polyline = new DxfLwPolyline(boundary.Points.Select((point, index) => new DxfLwPolylineVertex
          {
            X = Snap(point.X),
            Y = Snap(point.Y),
            Bulge = boundary.Bulges[index],
          }))
          {
            IsClosed = true,
            Layer = CutOuterLayer,
          };
          dxf.Entities.Add(polyline);
        }

        foreach (CommonCenterCurve common in cluster.CommonCurves)
        {
          if (common.GeometryKind == CommonLineGeometryKind.Line)
          {
            double dx = common.End.X - common.Start.X;
            double dy = common.End.Y - common.Start.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) <= sheetPlacement.GeometryTolerance)
            {
              throw new CommonLineValidationException("COMMON_LINE contains a zero-length or tiny segment.");
            }

            dxf.Entities.Add(new DxfLine(
              new DxfPoint(Snap(common.Start.X), Snap(common.Start.Y), 0D),
              new DxfPoint(Snap(common.End.X), Snap(common.End.Y), 0D))
            {
              Layer = CommonLineLayer,
            });
            continue;
          }

          if (common.GeometryKind != CommonLineGeometryKind.CircularArc
            || common.Center == null
            || common.Radius <= sheetPlacement.GeometryTolerance
            || Math.Abs(common.SweepRadians) * common.Radius <= sheetPlacement.GeometryTolerance)
          {
            throw new CommonLineValidationException("COMMON_LINE contains an invalid or unsupported exact curve.");
          }

          double sweep = common.SweepRadians;
          double angularTolerance = sheetPlacement.GeometryTolerance / common.Radius;
          if (Math.Abs(sweep) >= (2D * Math.PI) - angularTolerance)
          {
            dxf.Entities.Add(new DxfCircle(
              new DxfPoint(Snap(common.Center.X), Snap(common.Center.Y), 0D),
              Snap(common.Radius))
            {
              Layer = CommonLineLayer,
            });
            continue;
          }

          // A DXF ARC stores an implicit endpoint (center/radius/angle).  Independently rounding an
          // adjoining LINE therefore re-opens an otherwise canonical T/+ node after a save/load.
          // A two-vertex bulged LWPOLYLINE is the same exact circular primitive but stores both
          // endpoints explicitly, so every incident entity retains byte-identical node coordinates.
          dxf.Entities.Add(new DxfLwPolyline(new[]
          {
            new DxfLwPolylineVertex
            {
              X = Snap(common.Start.X),
              Y = Snap(common.Start.Y),
              Bulge = Math.Tan(sweep / 4D),
            },
            new DxfLwPolylineVertex
            {
              X = Snap(common.End.X),
              Y = Snap(common.End.Y),
            },
          })
          {
            IsClosed = false,
            Layer = CommonLineLayer,
          });
        }
      }

      if (offcutLines != null)
      {
        foreach (OffcutLine offcut in offcutLines)
        {
          dxf.Entities.Add(new DxfLine(
            new DxfPoint(offcut.X1, offcut.Y1, 0D),
            new DxfPoint(offcut.X2, offcut.Y2, 0D))
          {
            Layer = OffcutLayer,
          });
        }
      }

      EnsureSourceSnapshotsAreCurrent(sourceSnapshots, sheetPlacement.GeometryTolerance);
      ValidateCommonLineDxf(dxf, sheetPlacement.GeometryTolerance);

      var bendPlacements = sheetPlacement.PartPlacements
        .Where(p => p?.Part?.Name != null && DxfFileExtensions.IsDxf(p.Part.Name))
        .Select(p => (p.Part.Name, new DxfPoint(p.X, p.Y, 0D), p.Rotation, p.IsMirrored));
      AppendTransformedBendEntities(dxf, bendPlacements, sourceSnapshots);

      if (partNameExport != DxfPartNameExport.None)
      {
        AppendPartNameEntities(dxf, sheetPlacement.PartPlacements, partNameExport);
      }

      return dxf;
    }

    /// <summary>
    /// The resolver can preserve LINE and circular geometry from polylines, ARC/CIRCLE entities, or an
    /// exploded LINE+ARC contour. Keep this independent export-side guard so a future regression cannot
    /// suppress ELLIPSE/SPLINE/unknown geometry and silently replace it with chords.
    /// </summary>
    private static void EnsureClusterOuterSourceIsSupported(
      IReadOnlyCollection<DxfEntity> entities,
      int partId)
    {
      bool supported = entities != null
        && entities.Count > 0
        && entities.All(entity => entity is DxfLine
          || entity is DxfArc
          || entity is DxfCircle
          || entity is DxfLwPolyline
          || entity is DxfPolyline);
      if (!supported)
      {
        throw new CommonLineValidationException(
          $"Part {partId} has outside source geometry the common-line cluster cannot preserve exactly.");
      }
    }

    private static IReadOnlyDictionary<string, DxfSourceSnapshot> CaptureSourceSnapshots(
      IReadOnlyList<IPartPlacement> placements,
      double endpointTolerance)
    {
      var snapshots = new Dictionary<string, DxfSourceSnapshot>(StringComparer.OrdinalIgnoreCase);
      foreach (IPartPlacement placement in placements)
      {
        string source = placement?.Part?.Name;
#if NCRUNCH
        if (placement?.Part?.IsExact == true && !new FileInfo(source).Exists)
        {
          continue;
        }
#endif
        if (string.IsNullOrWhiteSpace(source)
          || !DxfFileExtensions.IsDxf(source))
        {
          throw new FileNotFoundException(
            $"The file {source} could not be loaded for validated common-line export.");
        }

        string path = Path.GetFullPath(source);
        if (!snapshots.ContainsKey(path))
        {
          try
          {
            snapshots.Add(path, DxfContourCatalog.ReadSnapshot(path, endpointTolerance));
          }
          catch (Exception ex)
          {
            throw new FileNotFoundException(
              $"The file {source} could not be loaded for validated common-line export.",
              ex);
          }
        }
      }

      return snapshots;
    }

    private static void EnsureSourceSnapshotsAreCurrent(
      IReadOnlyDictionary<string, DxfSourceSnapshot> snapshots,
      double endpointTolerance)
    {
      foreach (DxfSourceSnapshot snapshot in snapshots.Values)
      {
        try
        {
          DxfContourCatalog current = DxfContourCatalog.Load(
            snapshot.Path,
            endpointTolerance);
          if (!string.Equals(
            current.SourceFingerprint,
            snapshot.Catalog.SourceFingerprint,
            StringComparison.Ordinal))
          {
            throw new CommonLineValidationException(
              $"Source DXF '{snapshot.Path}' changed during common-line export; export was cancelled.");
          }
        }
        catch (CommonLineValidationException)
        {
          throw;
        }
        catch (Exception)
        {
          throw new CommonLineValidationException(
            $"Source DXF '{snapshot.Path}' changed or became unavailable during common-line export.");
        }
      }
    }

    private static IEnumerable<(bool IsChild, List<DxfEntity> Entities)> LoadPlacedContours(
      IPartPlacement placement,
      IReadOnlyDictionary<string, DxfSourceSnapshot> sourceSnapshots)
    {
      RawDetail<DxfEntity> detail;
      string source = placement.Part.Name;
      try
      {
#if NCRUNCH
        if (placement.Part.IsExact && !new FileInfo(source).Exists)
        {
          detail = DxfParser.ConvertDxfToRawDetail("generated.dxf", placement.Part.ToDxfFile().Entities.ToArray());
        }
        else
#endif
        {
          if (string.IsNullOrWhiteSpace(source) || !DxfFileExtensions.IsDxf(source))
          {
            throw new FileNotFoundException();
          }

          string path = Path.GetFullPath(source);
          if (!sourceSnapshots.TryGetValue(path, out DxfSourceSnapshot snapshot))
          {
            throw new FileNotFoundException();
          }

          detail = DxfParser.ConvertDxfToRawDetail(
            source,
            snapshot.OpenFile().Entities.ToArray());
        }
      }
      catch
      {
        throw new FileNotFoundException($"The file {source} could not be loaded for validated common-line export.");
      }

      // SheetPlacement coordinates are local to their sheet. Sheet.Id is identity metadata, not a
      // horizontal page index; applying Width * Id here shifts source contours away from the resolved
      // cluster geometry whenever an application assigns a non-zero/non-contiguous sheet id.
      var offset = new DxfPoint(placement.X, placement.Y, 0D);
      foreach (ILocalContour contour in detail.Outers)
      {
        if (contour is not LocalContour<DxfEntity> cast)
        {
          throw new CommonLineValidationException("A DXF contour could not be reconstructed for common-line export.");
        }

        List<DxfEntity> transformed = OffsetToNest(
          cast.Entities.ToList(),
          offset,
          placement.Rotation,
          placement.IsMirrored);
        yield return (cast.IsChild, SnapToGrid(transformed).ToList());
      }
    }

    private static List<DxfEntity> EnsureClosedContour(
      List<DxfEntity> entities,
      string layer,
      double geometryTolerance)
    {
      if (entities == null || entities.Count == 0)
      {
        throw new CommonLineValidationException($"{layer} contains an empty contour.");
      }

      if (entities.Count == 1)
      {
        if (entities[0] is DxfLwPolyline lightweight)
        {
          ClosePolyline(lightweight, geometryTolerance);
        }
        else if (entities[0] is DxfPolyline polyline)
        {
          ClosePolyline(polyline, geometryTolerance);
        }
        else if (entities[0] is DxfSpline spline
          && !spline.IsClosed
          && SplineDeclaresClosed(spline, geometryTolerance))
        {
          // Some CAD writers repeat the endpoint but omit bit 1 of the SPLINE flags. The source curve
          // is geometrically closed; set the DXF flag so a reader receives an explicitly closed contour.
          spline.IsClosed = true;
        }
      }

      if (entities.Count != 1 || !IsClosedCutEntity(entities[0], geometryTolerance))
      {
        try
        {
          FlatDxfJoiner.JoinEntities(entities, geometryTolerance);
        }
        catch
        {
          throw new CommonLineValidationException($"{layer} could not be rebuilt as a closed contour.");
        }
      }

      if (entities.Count != 1 || !IsClosedCutEntity(entities[0], geometryTolerance))
      {
        throw new CommonLineValidationException($"{layer} contains geometry that is not completely closed.");
      }

      entities[0].Layer = layer;
      return entities;
    }

    private static void ClosePolyline(DxfLwPolyline polyline, double geometryTolerance)
    {
      if (polyline.IsClosed)
      {
        return;
      }

      if (polyline.Vertices.Count > 3
        && Coincide(
          new DxfPoint(polyline.Vertices[0].X, polyline.Vertices[0].Y, 0D),
          new DxfPoint(polyline.Vertices[^1].X, polyline.Vertices[^1].Y, 0D),
          geometryTolerance))
      {
        polyline.Vertices.RemoveAt(polyline.Vertices.Count - 1);
        polyline.IsClosed = true;
      }
    }

    private static void ClosePolyline(DxfPolyline polyline, double geometryTolerance)
    {
      if (polyline.IsClosed)
      {
        return;
      }

      if (polyline.Vertices.Count > 3
        && Coincide(polyline.Vertices[0].Location, polyline.Vertices[^1].Location, geometryTolerance))
      {
        polyline.Vertices.RemoveAt(polyline.Vertices.Count - 1);
        polyline.IsClosed = true;
      }
    }

    private static bool IsClosedCutEntity(DxfEntity entity, double geometryTolerance)
      => (entity is DxfCircle circle
          && IsFinitePoint(circle.Center)
          && double.IsFinite(circle.Radius)
          && circle.Radius > geometryTolerance)
        || (entity is DxfEllipse ellipse
          && IsFinitePoint(ellipse.Center)
          && double.IsFinite(ellipse.MajorAxis.X)
          && double.IsFinite(ellipse.MajorAxis.Y)
          && double.IsFinite(ellipse.MajorAxis.Z)
          && IsFullUsableEllipse(ellipse, geometryTolerance))
        || (entity is DxfSpline spline
          && SplineDeclaresClosed(spline, geometryTolerance)
          && TrySampleSpline(spline, geometryTolerance, out _))
        || (entity is DxfLwPolyline lightweight
          && IsUsableClosedPolyline(lightweight, geometryTolerance))
        || (entity is DxfPolyline polyline
          && IsUsableClosedPolyline(polyline, geometryTolerance));

    private static bool IsUsableClosedPolyline(
      DxfLwPolyline polyline,
      double geometryTolerance)
    {
      if (!polyline.IsClosed
        || polyline.Vertices.Count < 3
        || !double.IsFinite(polyline.Elevation)
        || polyline.Vertices.Any(vertex => !double.IsFinite(vertex.X)
          || !double.IsFinite(vertex.Y)
          || !double.IsFinite(vertex.Bulge)))
      {
        return false;
      }

      for (int i = 0; i < polyline.Vertices.Count; i++)
      {
        DxfLwPolylineVertex start = polyline.Vertices[i];
        DxfLwPolylineVertex end = polyline.Vertices[(i + 1) % polyline.Vertices.Count];
        if (!IsUsableChord(start.X, start.Y, end.X, end.Y, geometryTolerance))
        {
          return false;
        }
      }

      return true;
    }

    private static bool IsUsableClosedPolyline(
      DxfPolyline polyline,
      double geometryTolerance)
    {
      if (!polyline.IsClosed
        || polyline.Vertices.Count < 3
        || polyline.Vertices.Any(vertex => !IsFinitePoint(vertex.Location)
          || !double.IsFinite(vertex.Bulge)))
      {
        return false;
      }

      for (int i = 0; i < polyline.Vertices.Count; i++)
      {
        DxfPoint start = polyline.Vertices[i].Location;
        DxfPoint end = polyline.Vertices[(i + 1) % polyline.Vertices.Count].Location;
        if (!IsUsableChord(start.X, start.Y, end.X, end.Y, geometryTolerance))
        {
          return false;
        }
      }

      return true;
    }

    private static bool IsUsableChord(
      double startX,
      double startY,
      double endX,
      double endY,
      double geometryTolerance)
    {
      double dx = endX - startX;
      double dy = endY - startY;
      double length = Math.Sqrt((dx * dx) + (dy * dy));
      return double.IsFinite(length) && length > geometryTolerance;
    }

    private static bool IsFullUsableEllipse(DxfEllipse ellipse, double tolerance)
    {
      double major = Math.Sqrt(
        (ellipse.MajorAxis.X * ellipse.MajorAxis.X)
        + (ellipse.MajorAxis.Y * ellipse.MajorAxis.Y));
      double minor = major * Math.Abs(ellipse.MinorAxisRatio);
      double sweep = Math.Abs(ellipse.EndParameter - ellipse.StartParameter);
      double angularTolerance = Math.Max(1e-10, tolerance / Math.Max(major, tolerance));
      return double.IsFinite(major)
        && double.IsFinite(minor)
        && major > tolerance
        && minor > tolerance
        && double.IsFinite(sweep)
        && Math.Abs(sweep - (2D * Math.PI)) <= angularTolerance;
    }

    private static bool SplineDeclaresClosed(DxfSpline spline, double tolerance)
    {
      if (spline.IsClosed || spline.IsPeriodic)
      {
        return true;
      }

      if (spline.FitPoints.Count >= 2
        && Coincide(spline.FitPoints[0], spline.FitPoints[^1], tolerance))
      {
        return true;
      }

      return spline.ControlPoints.Count >= 2
        && Coincide(
          spline.ControlPoints[0].Point,
          spline.ControlPoints[^1].Point,
          tolerance);
    }

    private static bool TrySampleSpline(
      DxfSpline spline,
      double tolerance,
      out List<DxfPoint> points)
    {
      points = new List<DxfPoint>();
      try
      {
        RawDetail<DxfEntity> sampled = DxfParser.ConvertDxfToRawDetail(
          "common-line-validation.dxf",
          new DxfEntity[] { spline });
        ILocalContour contour = sampled.Outers.SingleOrDefault();
        if (contour == null)
        {
          return false;
        }

        points.AddRange(contour.Points.Select(point => new DxfPoint(point.X, point.Y, 0D)));
        if (points.Count > 1 && Coincide(points[0], points[^1], tolerance))
        {
          points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3 || points.Any(point => !IsFinitePoint(point)))
        {
          return false;
        }

        for (int i = 0; i < points.Count; i++)
        {
          DxfPoint start = points[i];
          DxfPoint end = points[(i + 1) % points.Count];
          if (!IsUsableChord(start.X, start.Y, end.X, end.Y, tolerance))
          {
            return false;
          }
        }

        return true;
      }
      catch
      {
        points.Clear();
        return false;
      }
    }

    private static bool Coincide(DxfPoint a, DxfPoint b, double geometryTolerance)
      => Math.Abs(a.X - b.X) <= geometryTolerance
        && Math.Abs(a.Y - b.Y) <= geometryTolerance;

    internal static void ValidateCommonLineDxf(DxfFile dxf, double geometryTolerance)
    {
      foreach (DxfEntity entity in dxf.Entities)
      {
        if (entity.Layer == CutInnerLayer || entity.Layer == CutOuterLayer)
        {
          if (!IsClosedCutEntity(entity, geometryTolerance))
          {
            throw new CommonLineValidationException(
              $"{entity.Layer} contains an open, non-finite, tiny, or otherwise invalid entity.");
          }

          if (entity.Layer == CutOuterLayer
            && IsSelfIntersectingCutOuter(entity, geometryTolerance))
          {
            throw new CommonLineValidationException("CUT_OUTER contains a self-intersecting contour.");
          }
        }
        else if (entity.Layer == CommonLineLayer)
        {
          if (entity is DxfLine line)
          {
            double dx = line.P2.X - line.P1.X;
            double dy = line.P2.Y - line.P1.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (!IsFinitePoint(line.P1)
              || !IsFinitePoint(line.P2)
              || !double.IsFinite(length)
              || length <= geometryTolerance)
            {
              throw new CommonLineValidationException(
                "COMMON_LINE contains a non-finite, zero-length, or tiny LINE entity.");
            }
          }
          else if (entity is DxfArc arc)
          {
            if (!IsFinitePoint(arc.Center)
              || !double.IsFinite(arc.Radius)
              || !double.IsFinite(arc.StartAngle)
              || !double.IsFinite(arc.EndAngle))
            {
              throw new CommonLineValidationException(
                "COMMON_LINE contains a non-finite ARC center, radius, or angle.");
            }

            double rawSweep = arc.EndAngle - arc.StartAngle;
            if (!double.IsFinite(rawSweep))
            {
              throw new CommonLineValidationException(
                "COMMON_LINE contains ARC angles whose sweep is non-finite.");
            }

            double sweep = rawSweep % 360D;
            if (sweep <= 0D)
            {
              sweep += 360D;
            }

            double arcLength = arc.Radius * sweep * Math.PI / 180D;
            if (arc.Radius <= geometryTolerance
              || !double.IsFinite(sweep)
              || !double.IsFinite(arcLength)
              || arcLength <= geometryTolerance)
            {
              throw new CommonLineValidationException("COMMON_LINE contains a zero-length or invalid ARC.");
            }
          }
          else if (entity is DxfCircle circle)
          {
            if (!IsFinitePoint(circle.Center)
              || !double.IsFinite(circle.Radius)
              || circle.Radius <= geometryTolerance)
            {
              throw new CommonLineValidationException(
                "COMMON_LINE contains a non-finite, zero-length, or invalid CIRCLE.");
            }
          }
          else if (entity is DxfLwPolyline lightweight)
          {
            if (lightweight.Vertices.Count < 2)
            {
              throw new CommonLineValidationException("COMMON_LINE contains a degenerate LWPOLYLINE.");
            }

            int segmentCount = lightweight.IsClosed
              ? lightweight.Vertices.Count
              : lightweight.Vertices.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
              DxfLwPolylineVertex start = lightweight.Vertices[i];
              DxfLwPolylineVertex end = lightweight.Vertices[(i + 1) % lightweight.Vertices.Count];
              double dx = end.X - start.X;
              double dy = end.Y - start.Y;
              if (!double.IsFinite(start.X)
                || !double.IsFinite(start.Y)
                || !double.IsFinite(end.X)
                || !double.IsFinite(end.Y)
                || !double.IsFinite(start.Bulge)
                || Math.Sqrt((dx * dx) + (dy * dy)) <= geometryTolerance)
              {
                throw new CommonLineValidationException("COMMON_LINE contains an invalid or tiny LWPOLYLINE segment.");
              }
            }
          }
          else
          {
            throw new CommonLineValidationException(
              "COMMON_LINE contains an entity other than LINE, circular LWPOLYLINE, ARC, or CIRCLE.");
          }
        }
      }
    }

    private static bool IsFinitePoint(DxfPoint point)
      => double.IsFinite(point.X)
        && double.IsFinite(point.Y)
        && double.IsFinite(point.Z);

    private static bool IsSelfIntersectingCutOuter(DxfEntity entity, double tolerance)
    {
      List<(DxfPoint Start, DxfPoint End, double Bulge)> segments;
      if (entity is DxfLwPolyline lightweight)
      {
        segments = Enumerable.Range(0, lightweight.Vertices.Count)
          .Select(i =>
          {
            DxfLwPolylineVertex start = lightweight.Vertices[i];
            DxfLwPolylineVertex end = lightweight.Vertices[(i + 1) % lightweight.Vertices.Count];
            return (
              new DxfPoint(start.X, start.Y, 0D),
              new DxfPoint(end.X, end.Y, 0D),
              start.Bulge);
          })
          .ToList();
      }
      else if (entity is DxfPolyline polyline)
      {
        segments = Enumerable.Range(0, polyline.Vertices.Count)
          .Select(i =>
          {
            DxfVertex start = polyline.Vertices[i];
            DxfVertex end = polyline.Vertices[(i + 1) % polyline.Vertices.Count];
            return (start.Location, end.Location, start.Bulge);
          })
          .ToList();
      }
      else if (entity is DxfSpline spline
        && TrySampleSpline(spline, tolerance, out List<DxfPoint> splinePoints))
      {
        segments = Enumerable.Range(0, splinePoints.Count)
          .Select(i => (
            splinePoints[i],
            splinePoints[(i + 1) % splinePoints.Count],
            0D))
          .ToList();
      }
      else
      {
        // A usable full ellipse and circle are inherently simple; unsupported/invalid entities have
        // already failed IsClosedCutEntity before this method is reached.
        return false;
      }

      double effectiveTolerance = Math.Max(0D, tolerance);
      var sampled = new List<List<(DxfPoint Start, DxfPoint End)>>(segments.Count);
      foreach ((DxfPoint Start, DxfPoint End, double Bulge) segment in segments)
      {
        if (CutDistance(segment.Start, segment.End) <= effectiveTolerance)
        {
          return true;
        }

        sampled.Add(SampleCutSegment(segment.Start, segment.End, segment.Bulge, effectiveTolerance));
      }

      for (int i = 0; i < sampled.Count; i++)
      {
        int iNext = (i + 1) % sampled.Count;
        for (int j = i + 1; j < sampled.Count; j++)
        {
          int jNext = (j + 1) % sampled.Count;
          if (iNext == j || jNext == i)
          {
            continue; // neighbouring source segments intentionally meet at their shared vertex
          }

          if (sampled[i].Any(first => sampled[j].Any(second =>
            CutSegmentsConflict(first.Start, first.End, second.Start, second.End, effectiveTolerance))))
          {
            return true;
          }
        }
      }

      return false;
    }

    private static List<(DxfPoint Start, DxfPoint End)> SampleCutSegment(
      DxfPoint start,
      DxfPoint end,
      double bulge,
      double tolerance)
    {
      if (Math.Abs(bulge) < 1e-12)
      {
        return new List<(DxfPoint, DxfPoint)> { (start, end) };
      }

      double theta = 4D * Math.Atan(bulge);
      double dx = end.X - start.X;
      double dy = end.Y - start.Y;
      double chord = Math.Sqrt((dx * dx) + (dy * dy));
      double sinHalf = Math.Sin(theta / 2D);
      if (chord <= tolerance || Math.Abs(sinHalf) < 1e-12)
      {
        return new List<(DxfPoint, DxfPoint)> { (start, end) };
      }

      double signedRadius = chord / (2D * sinHalf);
      double radius = Math.Abs(signedRadius);
      double midX = (start.X + end.X) / 2D;
      double midY = (start.Y + end.Y) / 2D;
      double apothem = signedRadius * Math.Cos(theta / 2D);
      double centerX = midX + (apothem * (-dy / chord));
      double centerY = midY + (apothem * (dx / chord));
      double firstAngle = Math.Atan2(start.Y - centerY, start.X - centerX);

      // Match the parser's practical 1..6 degree tessellation while using the active tolerance to
      // tighten ordinary-sized arcs. This keeps validation bounded for very large-radius CAD geometry.
      double chordTolerance = Math.Max(1e-9, tolerance);
      double cosHalf = 1D - (chordTolerance / radius);
      cosHalf = Math.Max(-1D, Math.Min(1D, cosHalf));
      double step = 2D * Math.Acos(cosHalf);
      step = Math.Max(Math.PI / 180D, Math.Min(Math.PI / 30D, step));
      int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(theta) / step));

      var result = new List<(DxfPoint Start, DxfPoint End)>(count);
      DxfPoint prior = start;
      for (int i = 1; i <= count; i++)
      {
        double angle = firstAngle + ((theta * i) / count);
        DxfPoint next = i == count
          ? end
          : new DxfPoint(
            centerX + (radius * Math.Cos(angle)),
            centerY + (radius * Math.Sin(angle)),
            0D);
        result.Add((prior, next));
        prior = next;
      }

      return result;
    }

    private static bool CutSegmentsConflict(
      DxfPoint a,
      DxfPoint b,
      DxfPoint c,
      DxfPoint d,
      double tolerance)
    {
      double abC = CutCross(a, b, c);
      double abD = CutCross(a, b, d);
      double cdA = CutCross(c, d, a);
      double cdB = CutCross(c, d, b);
      double scale = Math.Max(1D, Math.Max(CutDistance(a, b), CutDistance(c, d)));
      double epsilon = tolerance * scale;
      if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
        && ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon)))
      {
        return true;
      }

      return (Math.Abs(abC) <= epsilon && CutPointOnSegment(c, a, b, tolerance))
        || (Math.Abs(abD) <= epsilon && CutPointOnSegment(d, a, b, tolerance))
        || (Math.Abs(cdA) <= epsilon && CutPointOnSegment(a, c, d, tolerance))
        || (Math.Abs(cdB) <= epsilon && CutPointOnSegment(b, c, d, tolerance));
    }

    private static bool CutPointOnSegment(DxfPoint point, DxfPoint start, DxfPoint end, double tolerance)
      => point.X >= Math.Min(start.X, end.X) - tolerance
        && point.X <= Math.Max(start.X, end.X) + tolerance
        && point.Y >= Math.Min(start.Y, end.Y) - tolerance
        && point.Y <= Math.Max(start.Y, end.Y) + tolerance;

    private static double CutCross(DxfPoint a, DxfPoint b, DxfPoint point)
      => ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));

    private static double CutDistance(DxfPoint a, DxfPoint b)
    {
      double dx = b.X - a.X;
      double dy = b.Y - a.Y;
      return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void AddCommonLineLayers(DxfFile dxf)
    {
      AddLayer(dxf, CutInnerLayer, DxfColorHelpers.GetClosestDefaultIndexColor(255, 0, 0));
      AddLayer(dxf, CommonLineLayer, DxfColorHelpers.GetClosestDefaultIndexColor(0, 255, 0));
      AddLayer(dxf, CutOuterLayer, DxfColorHelpers.GetClosestDefaultIndexColor(0, 0, 255));
      AddLayer(dxf, OffcutLayer, DxfColorHelpers.GetClosestDefaultIndexColor(255, 255, 0));
    }

    private static void AddLayer(DxfFile dxf, string name, DxfColor color)
    {
      if (!dxf.Layers.Any(layer => string.Equals(layer.Name, name, StringComparison.Ordinal)))
      {
        dxf.Layers.Add(new DxfLayer(name, color));
      }
    }

    private DxfFile GenerateDxfFile(
      IEnumerable<INfp> polygons,
      ISheet sheet,
      int i,
      bool doMergeLines,
      IReadOnlyCollection<OffcutLine> offcutLines = null,
      DxfPartNameExport partNameExport = DxfPartNameExport.None)
    {
      try
      {
        DxfFile sheetdxf = GenerateEmptyDxfFile();
        IEnumerable<DxfEntity> entities = GetOffsetDxfEntities(polygons.Where(o => o.Sheet.Id == sheet.Id), sheet, i).ToList();

        // Snap every coordinate to the micro-inch BEFORE merging: the per-part double transforms
        // leave coincident geometry (a common-line shared edge) ~1e-8 apart — irrelevant to the cut
        // but visible when measured in CAD, and noise the merger's tolerance has to absorb. On the
        // 1e-6 grid, coincident geometry is numerically IDENTICAL.
        entities = SnapToGrid(entities).ToList();

        if (doMergeLines)
        {
          entities = new DxfLineMerger().MergeLines(entities);
          //Have seen oddness where splitting out verticals and non verticals prevented an errant output; but then it just stopped happening!
          //var mergeLines = entities.Where(o => o is DxfLine).Cast<DxfLine>().Select(o => new MergeLine(o));
          //var over = mergeLines.Where(o => o.IsVertical && Math.Max(o.Left.Y, o.Right.Y) > sheet.MaxY);
          //var under = mergeLines.Where(o => o.IsVertical && Math.Min(o.Left.Y, o.Right.Y) < sheet.MinY);
          //if (over.Any() || under.Any())
          //{
          //  System.Diagnostics.Debugger.Break();
          //}

          //entities = mergeLines.Where(o => !o.IsVertical).Select(o => o.Line as DxfEntity).ToList();
          //entities.AddRange(mergeLines.Where(o => o.IsVertical).Select(o => o.Line));
        }

        // AFTER the merge (order matters): chain the exploded LINE/ARC soup into closed LWPOLYLINEs
        // so the laser receives real closed contours. Common-line sheets keep their merged shared
        // edges — a shared edge belongs to two contours, so those chains hit the joiner's
        // ambiguous-junction guard and stay as loose (already merged) lines, which is the correct
        // cut geometry. A join failure must never break the export.
        var entityList = entities as List<DxfEntity> ?? entities.ToList();
        try
        {
          FlatDxfJoiner.JoinEntities(entityList);
        }
        catch
        {
          // keep the unjoined entities
        }

        foreach (var entity in entityList)
        {
          // Cut geometry keeps the source drawing's layer here, and a drawing that happened to draw its outline
          // on the layer the sheet uses for marking would otherwise be engraved instead of cut.
          if (entity.Layer == DxfOutputLayers.Paint.Name || entity.Layer == DxfOutputLayers.Background.Name)
          {
            entity.Layer = DxfOutputLayers.Cut.Name;
          }

          sheetdxf.Entities.Add(entity);
        }

        // The offcut separation cut(s) (rectangular-offcut nests): ordinary cut geometry, added
        // AFTER the merge/join so they can never be fused into a part contour.
        if (offcutLines != null)
        {
          foreach (var offcutLine in offcutLines)
          {
            sheetdxf.Entities.Add(new DxfLine(
              new DxfPoint(offcutLine.X1, offcutLine.Y1, 0),
              new DxfPoint(offcutLine.X2, offcutLine.Y2, 0)));
          }
        }

        var sheetXoffset = -sheet.WidthCalculated * i;
        var bendPlacements = polygons
          .Where(o => o.Fitted != false && o.Sheet.Id == sheet.Id && o.Name != null && DxfFileExtensions.IsDxf(o.Name))
          .Select(p => (p.Name, new DxfPoint(p.X + sheetXoffset, p.Y, 0D), p.Rotation, p.IsMirrored));
        AppendTransformedBendEntities(sheetdxf, bendPlacements);

        if (partNameExport != DxfPartNameExport.None)
        {
          AppendPartNameEntities(sheetdxf, polygons, sheet.Id, sheetXoffset, partNameExport);
        }

        return sheetdxf;
      }
      catch (Exception ex)
      {
        throw;
      }
    }

    private sealed class PartBendInfo
    {
      public List<DxfEntity> TemplateEntities { get; set; } = new List<DxfEntity>();
      public string LayerName { get; set; }
      public DxfLayer LayerDef { get; set; }
      public List<DxfLineType> LineTypes { get; set; } = new List<DxfLineType>();

      /// <summary>Lines on the inner-cut layer: cut through, on the cut layer, not marked.</summary>
      public List<DxfEntity> InnerCutEntities { get; set; } = new List<DxfEntity>();
    }

    private static bool IsBendEntity(DxfEntity entity)
    {
      return entity is DxfLine
        || entity is DxfLwPolyline
        || entity is DxfPolyline
        || entity is DxfArc
        || entity is DxfCircle
        || entity is DxfSpline
        || entity is DxfEllipse;
    }

    private static (string Name, DxfLayer LayerDef) ResolveBendLayer(
      DxfFile sourceDxf,
      IReadOnlyList<DxfEntity> bendEntities)
    {
      if (sourceDxf == null)
      {
        return ("BEND", null);
      }

      if (bendEntities != null)
      {
        foreach (var e in bendEntities)
        {
          if (!string.IsNullOrWhiteSpace(e.Layer) && !string.Equals(e.Layer, "0", StringComparison.OrdinalIgnoreCase))
          {
            var matchedLayer = sourceDxf.Layers.FirstOrDefault(l => string.Equals(l.Name, e.Layer, StringComparison.OrdinalIgnoreCase));
            return (e.Layer, matchedLayer);
          }
        }
      }

      var namedBendLayer = sourceDxf.Layers.FirstOrDefault(l =>
        l.Name != null && (
          l.Name.IndexOf("BEND", StringComparison.OrdinalIgnoreCase) >= 0 ||
          l.Name.IndexOf("СГИБ", StringComparison.OrdinalIgnoreCase) >= 0 ||
          l.Name.IndexOf("FOLD", StringComparison.OrdinalIgnoreCase) >= 0 ||
          l.Name.IndexOf("PHANTOM", StringComparison.OrdinalIgnoreCase) >= 0 ||
          l.Name.IndexOf("MARK", StringComparison.OrdinalIgnoreCase) >= 0 ||
          l.Name.IndexOf("ETCH", StringComparison.OrdinalIgnoreCase) >= 0 ||
          string.Equals(l.Name, "9", StringComparison.OrdinalIgnoreCase)));

      if (namedBendLayer != null)
      {
        return (namedBendLayer.Name, namedBendLayer);
      }

      var secondLayer = sourceDxf.Layers.FirstOrDefault(l => !string.Equals(l.Name, "0", StringComparison.OrdinalIgnoreCase));
      if (secondLayer != null)
      {
        return (secondLayer.Name, secondLayer);
      }

      return ("BEND", null);
    }

    private static void EnsureBendLayer(DxfFile destination, string layerName, DxfLayer sourceLayerDef)
    {
      if (string.IsNullOrWhiteSpace(layerName))
      {
        return;
      }

      if (destination.Layers.Any(l => string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase)))
      {
        return;
      }

      var layer = new DxfLayer(layerName, sourceLayerDef?.Color ?? DxfColorHelpers.GetClosestDefaultIndexColor(255, 0, 0))
      {
        LineTypeName = sourceLayerDef?.LineTypeName ?? "Continuous",
        IsLayerOn = sourceLayerDef?.IsLayerOn ?? true,
      };

      destination.Layers.Add(layer);
    }

    private static void EnsureLineType(DxfFile destination, DxfLineType sourceLineType)
    {
      if (sourceLineType == null || string.IsNullOrWhiteSpace(sourceLineType.Name))
      {
        return;
      }

      if (destination.LineTypes.Any(lt => string.Equals(lt.Name, sourceLineType.Name, StringComparison.OrdinalIgnoreCase)))
      {
        return;
      }

      var copy = new DxfLineType(sourceLineType.Name)
      {
        Description = sourceLineType.Description,
        TotalPatternLength = sourceLineType.TotalPatternLength,
      };
      if (sourceLineType.Elements != null)
      {
        foreach (var elem in sourceLineType.Elements)
        {
          copy.Elements.Add(new DxfLineTypeElement
          {
            DashDotSpaceLength = elem.DashDotSpaceLength,
            ComplexFlags = elem.ComplexFlags,
            ShapeNumber = elem.ShapeNumber,
            RotationAngle = elem.RotationAngle,
            TextString = elem.TextString,
            Style = elem.Style,
            IsRotationAbsolute = elem.IsRotationAbsolute,
            IsEmbeddedElementAString = elem.IsEmbeddedElementAString,
            IsEmbeddedElementAShape = elem.IsEmbeddedElementAShape,
          });
        }
      }

      destination.LineTypes.Add(copy);
    }

    private static DxfFile LoadSourceDxf(
      string sourcePath,
      IReadOnlyDictionary<string, DxfSourceSnapshot> sourceSnapshots)
    {
      if (string.IsNullOrWhiteSpace(sourcePath))
      {
        return null;
      }

      if (sourceSnapshots != null)
      {
        try
        {
          string fullPath = Path.GetFullPath(sourcePath);
          if (sourceSnapshots.TryGetValue(fullPath, out var snapshot))
          {
            return snapshot.OpenFile();
          }
        }
        catch
        {
        }
      }

      try
      {
        if (File.Exists(sourcePath))
        {
          return DrawingFile.Load(sourcePath);
        }
      }
      catch
      {
      }

      return null;
    }

    private static PartBendInfo ExtractPartBendInfo(
      string sourcePath,
      IReadOnlyDictionary<string, DxfSourceSnapshot> sourceSnapshots)
    {
      try
      {
        DxfFile sourceDxf = LoadSourceDxf(sourcePath, sourceSnapshots);
        if (sourceDxf == null)
        {
          return null;
        }

        RawDetail<DxfEntity> detail = DxfParser.ConvertDxfToRawDetail(sourcePath, sourceDxf.Entities.ToArray());
        var cutEntities = new HashSet<DxfEntity>(
          detail.Outers.OfType<LocalContour<DxfEntity>>().SelectMany(c => c.Entities));

        // Open lines are bends, marked; those on the inner-cut layer are cut, like the outline.
        var innerCuts = sourceDxf.Entities.Where(e => DxfParser.IsInnerCut(e) && IsBendEntity(e)).ToList();
        var bendEntities = sourceDxf.Entities
          .Where(e => !cutEntities.Contains(e) && !DxfParser.IsAnnotation(e) && !DxfParser.IsInnerCut(e) && IsBendEntity(e))
          .ToList();

        if (bendEntities.Count == 0)
        {
          return new PartBendInfo { InnerCutEntities = innerCuts };
        }

        var (layerName, layerDef) = ResolveBendLayer(sourceDxf, bendEntities);

        var lineTypes = new List<DxfLineType>();
        if (layerDef != null && !string.IsNullOrWhiteSpace(layerDef.LineTypeName))
        {
          var lt = sourceDxf.LineTypes.FirstOrDefault(l => string.Equals(l.Name, layerDef.LineTypeName, StringComparison.OrdinalIgnoreCase));
          if (lt != null)
          {
            lineTypes.Add(lt);
          }
        }

        foreach (var ent in bendEntities)
        {
          if (!string.IsNullOrWhiteSpace(ent.LineTypeName) &&
              !string.Equals(ent.LineTypeName, "BYLAYER", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(ent.LineTypeName, "CONTINUOUS", StringComparison.OrdinalIgnoreCase))
          {
            var lt = sourceDxf.LineTypes.FirstOrDefault(l => string.Equals(l.Name, ent.LineTypeName, StringComparison.OrdinalIgnoreCase));
            if (lt != null && !lineTypes.Any(x => string.Equals(x.Name, lt.Name, StringComparison.OrdinalIgnoreCase)))
            {
              lineTypes.Add(lt);
            }
          }
        }

        return new PartBendInfo
        {
          TemplateEntities = bendEntities,
          LayerName = layerName,
          LayerDef = layerDef,
          LineTypes = lineTypes,
          InnerCutEntities = innerCuts,
        };
      }
      catch
      {
        return null;
      }
    }

    private static DxfEntity CloneBendEntity(DxfEntity entity, string defaultLayerName)
    {
      // Whatever layer the drawing kept its bends on, on the sheet they are marked, never cut.
      string targetLayer = defaultLayerName;

      DxfColor color = entity.Color != null && !entity.Color.IsByLayer && entity.Color.Index != 8 && entity.Color.Index != 7 && entity.Color.Index != 0
        ? entity.Color
        : DxfColor.ByLayer;

      string lineType = !string.IsNullOrWhiteSpace(entity.LineTypeName) &&
                        !string.Equals(entity.LineTypeName, "BYLAYER", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(entity.LineTypeName, "CONTINUOUS", StringComparison.OrdinalIgnoreCase)
        ? entity.LineTypeName
        : "BYLAYER";

      switch (entity)
      {
        case DxfLine line:
          return new DxfLine(new DxfPoint(line.P1.X, line.P1.Y, line.P1.Z), new DxfPoint(line.P2.X, line.P2.Y, line.P2.Z))
          {
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = line.LineTypeScale,
          };

        case DxfLwPolyline lw:
          var newLw = new DxfLwPolyline(lw.Vertices.Select(v => new DxfLwPolylineVertex
          {
            X = v.X,
            Y = v.Y,
            Bulge = v.Bulge,
            StartingWidth = v.StartingWidth,
            EndingWidth = v.EndingWidth,
          }))
          {
            IsClosed = lw.IsClosed,
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = lw.LineTypeScale,
          };
          return newLw;

        case DxfPolyline poly:
          var newPoly = new DxfPolyline(poly.Vertices.Select(v => new DxfVertex(new DxfPoint(v.Location.X, v.Location.Y, v.Location.Z))
          {
            Bulge = v.Bulge,
          }))
          {
            IsClosed = poly.IsClosed,
            Location = new DxfPoint(poly.Location.X, poly.Location.Y, poly.Location.Z),
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = poly.LineTypeScale,
          };
          return newPoly;

        case DxfArc arc:
          return new DxfArc(new DxfPoint(arc.Center.X, arc.Center.Y, arc.Center.Z), arc.Radius, arc.StartAngle, arc.EndAngle)
          {
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = arc.LineTypeScale,
          };

        case DxfCircle circle:
          return new DxfCircle(new DxfPoint(circle.Center.X, circle.Center.Y, circle.Center.Z), circle.Radius)
          {
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = circle.LineTypeScale,
          };

        case DxfSpline spline:
          var newSpline = new DxfSpline
          {
            DegreeOfCurve = spline.DegreeOfCurve,
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = spline.LineTypeScale,
          };
          foreach (var cp in spline.ControlPoints)
          {
            newSpline.ControlPoints.Add(new DxfControlPoint(new DxfPoint(cp.Point.X, cp.Point.Y, cp.Point.Z), cp.Weight));
          }
          foreach (var fp in spline.FitPoints)
          {
            newSpline.FitPoints.Add(new DxfPoint(fp.X, fp.Y, fp.Z));
          }
          foreach (var k in spline.KnotValues)
          {
            newSpline.KnotValues.Add(k);
          }
          return newSpline;

        case DxfEllipse ellipse:
          return new DxfEllipse(new DxfPoint(ellipse.Center.X, ellipse.Center.Y, ellipse.Center.Z), new DxfVector(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, ellipse.MajorAxis.Z), ellipse.MinorAxisRatio)
          {
            StartParameter = ellipse.StartParameter,
            EndParameter = ellipse.EndParameter,
            Layer = targetLayer,
            Color = color,
            LineTypeName = lineType,
            LineTypeScale = ellipse.LineTypeScale,
          };

        default:
          return null;
      }
    }

    private static void AppendTransformedBendEntities(
      DxfFile targetDxf,
      IEnumerable<(string SourcePath, DxfPoint Offset, double Rotation, bool IsMirrored)> placements,
      IReadOnlyDictionary<string, DxfSourceSnapshot> sourceSnapshots = null)
    {
      var bendCache = new Dictionary<string, PartBendInfo>(StringComparer.OrdinalIgnoreCase);

      foreach (var (sourcePath, offset, rotation, isMirrored) in placements)
      {
        if (string.IsNullOrWhiteSpace(sourcePath) || !DxfFileExtensions.IsDxf(sourcePath))
        {
          continue;
        }

        string cacheKey = sourcePath;
        try
        {
          cacheKey = Path.GetFullPath(sourcePath);
        }
        catch
        {
        }

        if (!bendCache.TryGetValue(cacheKey, out PartBendInfo info))
        {
          info = ExtractPartBendInfo(sourcePath, sourceSnapshots);
          bendCache[cacheKey] = info;
        }

        if (info?.InnerCutEntities?.Count > 0)
        {
          var cuts = info.InnerCutEntities
            .Select(e => CloneBendEntity(e, DxfOutputLayers.Cut.Name))
            .Where(e => e != null)
            .ToList();
          foreach (var cut in cuts)
          {
            cut.Color = DxfColor.ByLayer;
            cut.LineTypeName = "BYLAYER";
          }

          foreach (var ent in SnapToGrid(OffsetToNest(cuts, offset, rotation, isMirrored)))
          {
            targetDxf.Entities.Add(ent);
          }
        }

        if (info == null || info.TemplateEntities == null || info.TemplateEntities.Count == 0)
        {
          continue;
        }

        EnsureBendLayer(targetDxf, DxfOutputLayers.Paint.Name, info.LayerDef);
        if (info.LineTypes != null)
        {
          foreach (var lt in info.LineTypes)
          {
            EnsureLineType(targetDxf, lt);
          }
        }

        var clones = info.TemplateEntities
          .Select(e => CloneBendEntity(e, DxfOutputLayers.Paint.Name))
          .Where(e => e != null)
          .ToList();

        if (clones.Count == 0)
        {
          continue;
        }

        foreach (var clone in clones)
        {
          EnsureBendLayer(targetDxf, clone.Layer, info.LayerDef);
        }

        var transformed = OffsetToNest(clones, offset, rotation, isMirrored);
        var snapped = SnapToGrid(transformed);

        foreach (var ent in snapped)
        {
          targetDxf.Entities.Add(ent);
        }
      }
    }

    private static readonly System.Text.RegularExpressions.Regex FlatPatternPrefix = new System.Text.RegularExpressions.Regex(
      @"^\s*Flat[\s_-]*Pattern\s*-\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string GetPartNameLabel(string source)
    {
      if (string.IsNullOrWhiteSpace(source))
      {
        return string.Empty;
      }

      try
      {
        string name = Path.GetFileNameWithoutExtension(source);
        if (string.IsNullOrWhiteSpace(name))
        {
          name = Path.GetFileName(source);
        }

        // Sheet-metal CAD names every unfolded part "Flat-Pattern - <model>"; on the sheet only <model> tells
        // one part from another.
        string stripped = FlatPatternPrefix.Replace(name ?? string.Empty, string.Empty);
        return string.IsNullOrWhiteSpace(stripped) ? name ?? string.Empty : stripped;
      }
      catch
      {
        return Path.GetFileName(source) ?? source;
      }
    }

    private static void EnsurePartNameLayer(DxfFile targetDxf, string layerName, byte colorIndex)
    {
      if (string.IsNullOrWhiteSpace(layerName))
      {
        return;
      }

      if (targetDxf.Layers.Any(l => string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase)))
      {
        return;
      }

      var layer = new DxfLayer(layerName, DxfColor.FromIndex(colorIndex))
      {
        LineTypeName = "Continuous",
        IsLayerOn = true,
      };

      targetDxf.Layers.Add(layer);
    }

    public static (double X, double Y) ComputePartCentroid(INfp poly)
    {
      if (poly?.Points == null || poly.Points.Length < 3)
      {
        return (poly?.MinX ?? 0, poly?.MinY ?? 0);
      }

      var pts = poly.Points;
      double twiceArea = 0, cx = 0, cy = 0;
      for (int i = 0; i < pts.Length; i++)
      {
        var p = pts[i];
        var q = pts[(i + 1) % pts.Length];
        double cross = (p.X * q.Y) - (q.X * p.Y);
        twiceArea += cross;
        cx += (p.X + q.X) * cross;
        cy += (p.Y + q.Y) * cross;
      }

      if (Math.Abs(twiceArea) < 1e-12)
      {
        return ((poly.MinX + poly.MaxX) / 2.0, (poly.MinY + poly.MaxY) / 2.0);
      }

      double x = cx / (3.0 * twiceArea);
      double y = cy / (3.0 * twiceArea);

      if (GeometryUtil.PointInPolygon(new SvgPoint(x, y), poly) == true)
      {
        bool inHole = false;
        if (poly.Children != null)
        {
          foreach (var hole in poly.Children)
          {
            if (hole.Points != null && hole.Points.Length >= 3 && GeometryUtil.PointInPolygon(new SvgPoint(x, y), hole) == true)
            {
              inHole = true;
              break;
            }
          }
        }

        if (!inHole)
        {
          return (x, y);
        }
      }

      double midY = (poly.MinY + poly.MaxY) / 2.0;
      var intersections = new List<double>();
      for (int i = 0; i < pts.Length; i++)
      {
        var p1 = pts[i];
        var p2 = pts[(i + 1) % pts.Length];
        if ((p1.Y <= midY && p2.Y > midY) || (p2.Y <= midY && p1.Y > midY))
        {
          double t = (midY - p1.Y) / (p2.Y - p1.Y);
          intersections.Add(p1.X + (t * (p2.X - p1.X)));
        }
      }

      intersections.Sort();
      for (int i = 0; i + 1 < intersections.Count; i += 2)
      {
        double testX = (intersections[i] + intersections[i + 1]) / 2.0;
        if (GeometryUtil.PointInPolygon(new SvgPoint(testX, midY), poly) == true)
        {
          return (testX, midY);
        }
      }

      return ((poly.MinX + poly.MaxX) / 2.0, (poly.MinY + poly.MaxY) / 2.0);
    }

    internal static (double TargetHeight, bool IsVertical) ComputeTextProperties(INfp poly, string text)
    {
      double w = poly.MaxX - poly.MinX;
      double h = poly.MaxY - poly.MinY;
      bool isVertical = h > w * 1.4;

      double dimAlongText = isVertical ? h : w;
      double dimAcrossText = isVertical ? w : h;
      int textLen = Math.Max(1, text?.Length ?? 1);

      double maxHeightByLength = (dimAlongText * 0.75) / (0.6 * textLen);
      double maxHeightByThickness = dimAcrossText * 0.35;
      double computed = Math.Min(maxHeightByLength, maxHeightByThickness);

      double targetHeight;
      if (Math.Max(w, h) > 25.0)
      {
        targetHeight = Math.Clamp(computed, 1.5, 10.0);
      }
      else
      {
        targetHeight = Math.Clamp(computed, 0.06, 0.4);
      }

      return (targetHeight, isVertical);
    }

    private static void AppendPartNameEntities(
      DxfFile targetDxf,
      IEnumerable<IPartPlacement> placements,
      DxfPartNameExport exportMode)
    {
      if (exportMode == DxfPartNameExport.None || placements == null)
      {
        return;
      }

      var target = exportMode == DxfPartNameExport.Paint ? DxfOutputLayers.Paint : DxfOutputLayers.Background;
      string layerName = target.Name;
      byte colorIndex = (byte)target.ColorIndex;
      EnsurePartNameLayer(targetDxf, layerName, colorIndex);

      foreach (var placement in placements)
      {
        string text = GetPartNameLabel(placement.Part?.Name);
        if (string.IsNullOrWhiteSpace(text))
        {
          continue;
        }

        var poly = placement.PlacedPart;
        if (poly?.Points == null || poly.Points.Length < 3)
        {
          continue;
        }

        var (cx, cy) = ComputePartCentroid(poly);
        var (height, isVertical) = ComputeTextProperties(poly, text);

        var textEntity = new DxfText
        {
          Value = text,
          Location = new DxfPoint(Snap(cx), Snap(cy), 0),
          SecondAlignmentPoint = new DxfPoint(Snap(cx), Snap(cy), 0),
          HorizontalTextJustification = DxfHorizontalTextJustification.Center,
          VerticalTextJustification = DxfVerticalTextJustification.Middle,
          TextHeight = Snap(height),
          Rotation = isVertical ? 90.0 : 0.0,
          Layer = layerName,
          Color = DxfColor.ByLayer,
        };

        targetDxf.Entities.Add(textEntity);
      }
    }

    private static void AppendPartNameEntities(
      DxfFile targetDxf,
      IEnumerable<INfp> polygons,
      int sheetId,
      double sheetXoffset,
      DxfPartNameExport exportMode)
    {
      if (exportMode == DxfPartNameExport.None || polygons == null)
      {
        return;
      }

      var target = exportMode == DxfPartNameExport.Paint ? DxfOutputLayers.Paint : DxfOutputLayers.Background;
      string layerName = target.Name;
      byte colorIndex = (byte)target.ColorIndex;
      EnsurePartNameLayer(targetDxf, layerName, colorIndex);

      foreach (var polygon in polygons)
      {
        if (polygon.Fitted == false || polygon.Sheet?.Id != sheetId)
        {
          continue;
        }

        string text = GetPartNameLabel(polygon.Name);
        if (string.IsNullOrWhiteSpace(text))
        {
          continue;
        }

        double px = polygon.X + sheetXoffset;
        double py = polygon.Y;
        var placedPoly = polygon.Shift(px, py);
        if (placedPoly?.Points == null || placedPoly.Points.Length < 3)
        {
          continue;
        }

        var (cx, cy) = ComputePartCentroid(placedPoly);
        var (height, isVertical) = ComputeTextProperties(placedPoly, text);

        var textEntity = new DxfText
        {
          Value = text,
          Location = new DxfPoint(Snap(cx), Snap(cy), 0),
          SecondAlignmentPoint = new DxfPoint(Snap(cx), Snap(cy), 0),
          HorizontalTextJustification = DxfHorizontalTextJustification.Center,
          VerticalTextJustification = DxfVerticalTextJustification.Middle,
          TextHeight = Snap(height),
          Rotation = isVertical ? 90.0 : 0.0,
          Layer = layerName,
          Color = DxfColor.ByLayer,
        };

        targetDxf.Entities.Add(textEntity);
      }
    }
  }
}
