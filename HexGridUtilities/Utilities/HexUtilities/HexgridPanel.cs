﻿#region License - Copyright (C) 2012-2013 Pieter Geerkens, all rights reserved.
/////////////////////////////////////////////////////////////////////////////////////////
//                PG Software Solutions Inc. - Hex-Grid Utilities
//
// Use of this software is permitted only as described in the attached file: license.txt.
/////////////////////////////////////////////////////////////////////////////////////////
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using PG_Napoleonics.Utilities.HexUtilities;

namespace PG_Napoleonics.Utilities.HexUtilities {
  public interface IMapPanelHost {
    Size      GridSize           { get; }
    Size      MapSizePixels      { get; }

    Rectangle GetClipCells(PointF point, SizeF size);
    Rectangle GetClipCells(RectangleF visibleClipBounds);

    void      PaintHighlight(Graphics g);
    void      PaintMap(Graphics g);
    void      PaintUnits(Graphics g);

    string    Name               { get; }   // only used in DebugTracing calls
  }

  public partial class HexgridPanel : Panel, ISupportInitialize {
    public HexgridPanel() {
      InitializeComponent();
    }
    public HexgridPanel(IContainer container) {
      container.Add(this);

      InitializeComponent();
    }

    #region ISupportInitialize implementation
    void ISupportInitialize.BeginInit() { }
    void ISupportInitialize.EndInit() { }
    #endregion

    public event EventHandler<EventArgs>   ScaleChange;
    protected virtual void OnScaleChange(EventArgs e) {
      var handler = ScaleChange;
      if( handler != null ) handler(this, e);
    }

    /// <summary>Form hosting this panel, which supplies various parameters and utilities.</summary>
    public IMapPanelHost Host             { get; set; }
    /// <summary>Margin of map in pixels.</summary>
    public Size          MapMargin        { get; set; }
    public Size          MapSizePixels    { get {return Host.MapSizePixels + MapMargin.Scale(2);} }
    /// <summary>Current scaling factor for map display.</summary>
    public float         MapScale         { get { return DesignMode ? 1 : Scales[ScaleIndex]; } }
    /// <summary>Index into <code>Scales</code> of current map scale.</summary>
    public virtual int   ScaleIndex       { 
      get { return _scaleIndex; }
      set { var newValue = Math.Max(0, Math.Min(Scales.Length-1, value));
            if( _scaleIndex != newValue) {
              _scaleIndex = newValue; 
              OnScaleChange(EventArgs.Empty); 
            }
      } 
    } int _scaleIndex;
    /// <summary>Array of supported map scales (float).</summary>
    public float[]       Scales           { 
      get {return _scales;}
      set {_scales = value; if (_scaleIndex!=0) ScaleIndex = _scaleIndex;}
    } float[] _scales;
    /// <summary>Returns, as a Rectangle, the IUserCoords for the currently visible extent.</summary>
    public Rectangle     VisibleRectangle {
      get { return Host.GetClipCells( AutoScrollPosition.Scale(-1.0F/MapScale), 
                                      ClientSize.Scale(1.0F/MapScale) );
      }
    }

    #region Grid Coordinates
    /// <summary>Scaled <code>Size</code> of each hexagon in grid, being the 'full-width' and 'full-height'.</summary>
    SizeF GridSize { get { return Host.GridSize.Scale(MapScale); } }

    /// Mathemagically (left as exercise for the reader) our 'picking' matrices are these, assuming: 
    ///  - origin at upper-left corner of hex (0,0);
    ///  - 'straight' hex-axis vertically down; and
    ///  - 'oblique'  hex-axis up-and-to-right (at 120 degrees from 'straight').
    private Matrix matrixX { 
      get { return new Matrix((3.0F/2.0F)/GridSize.Width,  (3.0F/2.0F)/GridSize.Width,
                                     1.0F/GridSize.Height,       -1.0F/GridSize.Height,  -0.5F,-0.5F); } 
    }
    private Matrix matrixY { 
      get { return new Matrix(       0.0F,                 (3.0F/2.0F)/GridSize.Width,
                                    2.0F/GridSize.Height,         1.0F/GridSize.Height,  -0.5F,-0.5F); } 
    }

    /// <summary>Canonical coordinates for a selected hex.</summary>
    /// <param name="point">Screen point specifying hex to be identified.</param>
    /// <returns>Canonical coordinates for a hex specified by a screen point.</returns>
    /// <see cref="HexGridAlgorithm.mht"/>
    public ICoordsCanon GetHexCoords(Point point) {
      return GetHexCoords(point, new Size(AutoScrollPosition));
    }
    /// <summary>Canonical coordinates for a selected hex for a given AutoScroll position.</summary>
    /// <param name="point">Screen point specifying hex to be identified.</param>
    /// <param name="autoScroll">AutoScrollPosition for game-display Panel.</param>
    /// <returns>Canonical coordinates for a hex specified by a screen point.</returns>
    /// <see cref="HexGridAlgorithm.mht"/>
    protected ICoordsCanon GetHexCoords(Point point, Size autoScroll) {
      if( Host == null ) return Coords.EmptyCanon;

      /// Adjust for origin not as assumed by GetCoordinate().
      var grid    = new Size((int)(GridSize.Width*2F/3F), (int)GridSize.Height);
      var margin  = new Size((int)(MapMargin.Width *MapScale), 
                             (int)(MapMargin.Height*MapScale));

      point      -= autoScroll + margin + grid;
      return Coords.NewCanonCoords( GetCoordinate(matrixX, point), 
                                    GetCoordinate(matrixY, point) );
    }

    /// <summary>Calculates a (canonical X or Y) grid-coordinate for a point, from the supplied 'picking' matrix.</summary>
    /// <param name="matrix">The 'picking' matrix</param>
    /// <param name="point">The screen point identifying the hex to be 'picked'.</param>
    /// <returns>A (canonical X or Y) grid coordinate of the 'picked' hex.</returns>
	  private static int GetCoordinate (Matrix matrix, Point point){
      var pts = new Point[] {point};
      matrix.TransformPoints(pts);
		  return (int) Math.Floor( (pts[0].X + pts[0].Y + 2F) / 3F );
	  }

    /// <summary>Returns the scroll position to center a specified hex in viewport.</summary>
    /// <param name="coordsNewCenterHex">ICoordsUser for the hex to be centered in viewport.</param>
    /// <returns>Pixel coordinates in Client reference frame.</returns>
    protected Point ScrollPositionToCenterOnHex(ICoordsUser coordsNewCenterHex) {
      return HexCenterPoint(Coords.NewUserCoords(
                coordsNewCenterHex.Vector - ( new IntVector2D(VisibleRectangle.Size) / 2 )
      ));
    }

    /// <summary>Returns ScrollPosition that places given hex in the upper-Left of viewport.</summary>
    /// <param name="coordsNewULHex">User (ie rectangular) hex-coordinates for new upper-left hex</param>
    /// <returns>Pixel coordinates in Client reference frame.</returns>
    public Point HexCenterPoint(ICoordsUser coordsNewULHex) {
      if (coordsNewULHex == null) return new Point();
      var offset = new Size((int)(GridSize.Width*2F/3F), (int)GridSize.Height);
      var margin = Size.Round( MapMargin.Scale(MapScale) );
      var point  = new Point(
        (int)(GridSize.Width  * coordsNewULHex.X),
        (int)(GridSize.Height * coordsNewULHex.Y   + GridSize.Height/2 * (coordsNewULHex.X+1)%2)
      );
      return point + margin + offset ;
    }
    #endregion

    #region Painting
    protected override void OnPaintBackground(PaintEventArgs e) { ; }
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);
      if(IsHandleCreated)    PaintPanel(e.Graphics);
    }
    protected virtual void PaintPanel(Graphics g) {
      if (DesignMode) { g.FillRectangle(Brushes.Gray, ClientRectangle);  return; }

      g.Clear(Color.White);
      g.DrawRectangle(Pens.Black, ClientRectangle);

      g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
      g.ScaleTransform(MapScale,MapScale);

      var state = g.Save();
      g.DrawImageUnscaled(Buffer, Point.Empty);

      g.Restore(state); state = g.Save();
      Host.PaintUnits(g);

      g.Restore(state); state = g.Save();
      Host.PaintHighlight(g);
    }
    #endregion

    #region Double-Buffering
    Bitmap Buffer        { 
      get { return _buffer ?? ( _buffer = PaintBuffer()); } 
    } Bitmap _buffer;
    Bitmap PaintBuffer() {
      var size   = MapSizePixels;
      var buffer = new Bitmap(size.Width,size.Height, PixelFormat.Format32bppPArgb);
      using(var g = Graphics.FromImage(buffer)) {
        Host.PaintMap(g);
      }
      return buffer;
    }
    #endregion
  }
}