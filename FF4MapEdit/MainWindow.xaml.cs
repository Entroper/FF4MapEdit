﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FF4;
using Microsoft.Win32;

namespace FF4MapEdit
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private FF4Rom _rom;
		private string _filename;

		private Tileset _tileset;
		private Map _map;

		private byte _selectedTile;
		private GeometryDrawing _selectedTileDrawing = new GeometryDrawing();
		private GeometryDrawing _gridLinesDrawing = new GeometryDrawing();
		private WriteableBitmap[] _rowBitmaps;

		private bool _painting = false;

		private struct PaintedTile
		{
			public int X, Y;
			public byte OldTile;
			public byte NewTile;
		}

		private List<PaintedTile> _paintingTiles = new List<PaintedTile>();
		private readonly Stack<List<PaintedTile>> _undoStack = new Stack<List<PaintedTile>>();
		private readonly Stack<List<PaintedTile>> _redoStack = new Stack<List<PaintedTile>>();

		private int SpaceUsed
		{
			set
			{
				int maxLength =
					_map.MapType == MapType.Overworld ? FF4Rom.OverworldRowDataMaxLength :
					_map.MapType == MapType.Underworld ? FF4Rom.UnderworldRowDataMaxLength :
					_map.MapType == MapType.Moon ? FF4Rom.MoonRowDataMaxLength : 0;

				SpaceUsedLabel.Content = $"Space used: {value}/{maxLength} bytes";
				SpaceUsedLabel.Foreground = value > FF4Rom.OverworldRowDataMaxLength ? Brushes.Red : Brushes.Black;
			}
		}

		public MainWindow()
		{
			InitializeComponent();

			WalkCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.Walk, true);
			ChocoboWalkCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.ChocoboWalk, true);
			BlackChocoboFlyCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.BlackChocoboFly, true);
			BlackChocoboLandCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.BlackChocoboLand, true);
			HovercraftCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.Hovercraft, true);
			AirshipFlyCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.AirshipFly, true);
			WalkPlateauCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.WalkPlateau, true);
			BigWhaleFlyCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.BigWhaleFly, true);
			ObscuresHalfCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.ObscuresHalf, true);
			AirshipLandCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.AirshipLand, true);
			EnemyEncountersCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.EnemyEncounters, true);
			TriggerCheckBox.Checked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.Trigger, true);

			WalkCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.Walk, false);
			ChocoboWalkCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.ChocoboWalk, false);
			BlackChocoboFlyCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.BlackChocoboFly, false);
			BlackChocoboLandCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.BlackChocoboLand, false);
			HovercraftCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.Hovercraft, false);
			AirshipFlyCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.AirshipFly, false);
			WalkPlateauCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.WalkPlateau, false);
			BigWhaleFlyCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.BigWhaleFly, false);
			ObscuresHalfCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.ObscuresHalf, false);
			AirshipLandCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.AirshipLand, false);
			EnemyEncountersCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.EnemyEncounters, false);
			TriggerCheckBox.Unchecked += (sender, e) => OnPropertyCheckBoxCheckChanged(WorldTileProperties.Trigger, false);
		}

		private void OnPropertyCheckBoxCheckChanged(WorldTileProperties property, bool isChecked)
		{
			if (isChecked)
			{
				_tileset.TileProperties[_selectedTile] |= (ushort)property;
			}
			else
			{
				_tileset.TileProperties[_selectedTile] &= (ushort)~property;
			}
		}

		private void OpenButton_Click(object sender, RoutedEventArgs e)
		{
			var openFileDialog = new OpenFileDialog
			{
				Filter = "SNES ROM files (*.sfc;*.smc)|*.sfc;*.smc|All Files (*.*)|*.*"
			};

			var result = openFileDialog.ShowDialog(this);
			if (result == false)
			{
				return;
			}

			_rom = new FF4Rom(openFileDialog.FileName);
			if (!_rom.Validate())
			{
				MessageBox.Show("ROM does not appear to be a US version 1.1 ROM.  Proceed at your own risk.", "Validation Failed");
			}

			_filename = openFileDialog.FileName;

			LoadWorldMap(MapType.Overworld);

			MapComboBox.Items.Add(new ComboBoxItem { Tag = MapType.Overworld, Content = "Overworld", IsSelected = true });
			MapComboBox.Items.Add(new ComboBoxItem { Tag = MapType.Underworld, Content = "Underworld" });
			MapComboBox.Items.Add(new ComboBoxItem { Tag = MapType.Moon, Content = "Moon" });
		}

		private void SaveButton_Click(object sender, RoutedEventArgs e)
		{
			if (_map != null)
			{
				SaveWorldMap();
				_rom.Save(_filename);
			}
		}

		private void MapComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var mapType = (MapType)MapComboBox.SelectedValue;
			if (mapType != _map.MapType)
			{
				SaveWorldMap();
				LoadWorldMap(mapType);
			}
		}

		private void LoadWorldMap(MapType mapType)
		{
			_map = _rom.LoadWorldMap(mapType);
			_tileset = _rom.LoadWorldMapTileset(mapType);

			LoadWorldMapTileset();
			LoadWorldMapTiles();

			SpaceUsed = _map.CompressedSize;

			SelectTile(0, 0);
			SidePanel.Visibility = Visibility.Visible;
		}

		private void LoadWorldMapTileset()
		{
			var tileGroup = new DrawingGroup();
			tileGroup.Children.Add(_selectedTileDrawing);

			for (int i = 0; i < FF4Rom.MapTileCount; i++)
			{
				tileGroup.Children.Add(new ImageDrawing(
					BitmapSource.Create(16, 16, 72, 72, PixelFormats.Bgr555, null, _tileset[i], 16 * 2),
					new Rect(new Point(16 * (i % 16), 16 * (i / 16)), new Size(16, 16))));
			}

			Tileset.Source = new DrawingImage(tileGroup);
		}

		private void LoadWorldMapTiles()
		{
			var rowGroup = new DrawingGroup();
			rowGroup.Open();

			_rowBitmaps = new WriteableBitmap[_map.Height];
			for (int y = 0; y < _map.Height; y++)
			{
				_rowBitmaps[y] = new WriteableBitmap(16 * _map.Width, 16, 72, 72, PixelFormats.Bgr555, null);
				_rowBitmaps[y].Lock();
				for (int x = 0; x < _map.Width; x++)
				{
					var tile = _map[y, x];
					_rowBitmaps[y].WritePixels(new Int32Rect(16 * x, 0, 16, 16), _tileset[tile], 16 * 2, 0);
				}

				_rowBitmaps[y].Unlock();

				rowGroup.Children.Add(new ImageDrawing(_rowBitmaps[y],
					new Rect(new Point(0, 16 * y), new Size(16 * _map.Width, 16))));
			}

			Map.Width = 16 * _map.Width;
			Map.Height = 16 * _map.Height;

			var geometry = new GeometryGroup();
			for (int x = 0; x <= _map.Width; x++)
			{
				geometry.Children.Add(new LineGeometry(new Point(16 * x, 0), new Point(16 * x, Map.Height)));
			}
			for (int y = 0; y <= _map.Height; y++)
			{
				geometry.Children.Add(new LineGeometry(new Point(0, 16 * y), new Point(Map.Width, 16 * y)));
			}
			_gridLinesDrawing = new GeometryDrawing
			{
				Geometry = geometry,
				Brush = Brushes.Transparent,
				Pen = new Pen(Brushes.Black, 1)
			};
			if (GridLinesButton.IsChecked == true)
			{
				rowGroup.Children.Add(_gridLinesDrawing);
			}

			Map.Source = new DrawingImage(rowGroup);
		}

		private void SaveWorldMap()
		{
			try
			{
				_rom.SaveWorldMap(_map);
				_rom.SaveWorldMapTileset(_map.MapType, _tileset);
			}
			catch (IndexOutOfRangeException ex) when (ex.Message.StartsWith("World map data is too big"))
			{
				MessageBox.Show(ex.Message, "Error while saving");
			}
		}

		private void Tileset_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			GetClickedTile(sender, e, out int x, out int y);
			if (x < 0 || x >= 16 || y < 0 || y >= 8)
			{
				return;
			}

			SelectTile(x, y);
		}

		private void SelectTile(int x, int y)
		{
			_selectedTile = (byte)(16 * y + x);

			HighlightSelectedTile(x, y);

			CheckTilePropertyBoxes();
		}

		private void HighlightSelectedTile(int x, int y)
		{
			var tileGroup = (DrawingGroup)((DrawingImage)Tileset.Source).Drawing;
			tileGroup.Children.Remove(_selectedTileDrawing);

			var geometry = new GeometryGroup();
			geometry.Children.Add(new RectangleGeometry(new Rect(new Point(16*x + 1, 16*y + 1), new Size(14, 14))));
			_selectedTileDrawing = new GeometryDrawing
			{
				Geometry = geometry,
				Brush = Brushes.Transparent,
				Pen = new Pen(Brushes.Aqua, 2)
			};

			tileGroup.Children.Add(_selectedTileDrawing);
		}

		private void GridLinesButton_OnChecked(object sender, RoutedEventArgs e)
		{
			var rowGroup = (DrawingGroup)((DrawingImage)Map.Source).Drawing;
			rowGroup.Children.Add(_gridLinesDrawing);
		}

		private void GridLinesButton_OnUnchecked(object sender, RoutedEventArgs e)
		{
			var rowGroup = (DrawingGroup)((DrawingImage)Map.Source).Drawing;
			rowGroup.Children.Remove(_gridLinesDrawing);
		}

		private void CheckTilePropertyBoxes()
		{
			var tileProperties = (WorldTileProperties)_tileset.TileProperties[_selectedTile];

			WalkCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.Walk);
			ChocoboWalkCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.ChocoboWalk);
			BlackChocoboFlyCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.BlackChocoboFly);
			BlackChocoboLandCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.BlackChocoboLand);
			HovercraftCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.Hovercraft);
			AirshipFlyCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.AirshipFly);
			WalkPlateauCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.WalkPlateau);
			BigWhaleFlyCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.BigWhaleFly);
			ObscuresHalfCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.ObscuresHalf);
			AirshipLandCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.AirshipLand);
			EnemyEncountersCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.EnemyEncounters);
			TriggerCheckBox.IsChecked = tileProperties.HasFlag(WorldTileProperties.Trigger);
		}

		private void Map_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			GetClickedTile(sender, e, out int x, out int y);
			if (x < 0 || x >= _map.Width || y < 0 || y >= _map.Height)
			{
				return;
			}

			if (FloodFillButton.IsChecked == true)
			{
				FloodFill(x, y);
			}
			else
			{
				Paint(x, y);
				_painting = true;
			}
		}

		private void Map_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			_painting = false;

			if (_paintingTiles.Count > 0)
			{
				RecompressMap(_paintingTiles);

				_undoStack.Push(_paintingTiles);
				_redoStack.Clear();
				_paintingTiles = new List<PaintedTile>();
			}
		}

		private void RecompressMap(List<PaintedTile> paintingTiles)
		{
			var rowsToCompress = paintingTiles.Select(tile => tile.Y).Distinct();
			foreach (var row in rowsToCompress)
			{
				_map.CompressRow(row);
			}

			SpaceUsed = _map.CompressedSize;
		}

		private void Map_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			GetClickedTile(sender, e, out int x, out int y);
			if (x < 0 || x >= _map.Width || y < 0 || y >= _map.Height || _map[y, x] == _selectedTile)
			{
				return;
			}

			var selectedTile = _map[y, x];
			SelectTile(selectedTile % 16, selectedTile / 16);
		}

		private void Map_MouseMove(object sender, MouseEventArgs e)
		{
			GetClickedTile(sender, e, out int x, out int y);
			CoordinatesLabel.Content = $"Coordinates: ({x:X2}, {y:X2})";

			if (_painting && !(x < 0 || x >= _map.Width || y < 0 || y >= _map.Height) && _map[y, x] != _selectedTile)
			{
				Paint(x, y);
			}
		}

		private void Paint(int x, int y)
		{
			_paintingTiles.Add(new PaintedTile
			{
				X = x,
				Y = y,
				OldTile = _map[y, x],
				NewTile = _selectedTile
			});

			SetTile(x, y, _selectedTile);
		}

		private void FloodFill(int x, int y)
		{
			var queue = new Queue<(int x, int y)>();
			var fillingTile = _map[y, x];
			if (fillingTile != _selectedTile)
			{
				Paint(x, y);
				queue.Enqueue((x, y));
			}

			while (queue.Count > 0)
			{
				(x, y) = queue.Dequeue();
				if (x > 0 && _map[y, x - 1] == fillingTile)
				{
					Paint(x - 1, y);
					queue.Enqueue((x - 1, y));
				}
				if (x < _map.Width - 1 && _map[y, x + 1] == fillingTile)
				{
					Paint(x + 1, y);
					queue.Enqueue((x + 1, y));
				}
				if (y > 0 && _map[y - 1, x] == fillingTile)
				{
					Paint(x, y - 1);
					queue.Enqueue((x, y - 1));
				}
				if (y < _map.Height - 1 && _map[y + 1, x] == fillingTile)
				{
					Paint(x, y + 1);
					queue.Enqueue((x, y + 1));
				}
			}
		}

		private void SetTile(int x, int y, byte tile)
		{
			_map[y, x] = tile;

			_rowBitmaps[y].Lock();
			_rowBitmaps[y].WritePixels(new Int32Rect(16 * x, 0, 16, 16), _tileset[tile], 16 * 2, 0);
			_rowBitmaps[y].Unlock();
		}

		private void GetClickedTile(object sender, MouseButtonEventArgs e, out int x, out int y)
		{
			var position = e.GetPosition((IInputElement)sender);
			PixelsToTile(position, out x, out y);
		}

		private void GetClickedTile(object sender, MouseEventArgs e, out int x, out int y)
		{
			var position = e.GetPosition((IInputElement)sender);
			PixelsToTile(position, out x, out y);
		}

		private static void PixelsToTile(Point position, out int x, out int y)
		{
			x = (int)position.X;
			y = (int)position.Y;
			x /= 16;
			y /= 16;
		}

		private void Undo_Executed(object sender, ExecutedRoutedEventArgs args)
		{
			Undo();
		}

		private void Redo_Executed(object sender, ExecutedRoutedEventArgs args)
		{
			Redo();
		}

		private void UndoButton_OnClick(object sender, RoutedEventArgs e)
		{
			Undo();
		}

		private void RedoButton_OnClick(object sender, RoutedEventArgs e)
		{
			Redo();
		}

		private void Undo()
		{
			if (_undoStack.Count > 0)
			{
				var undo = _undoStack.Pop();
				foreach (var painting in undo)
				{
					SetTile(painting.X, painting.Y, painting.OldTile);
				}

				_redoStack.Push(undo);

				RecompressMap(undo);
			}
		}

		private void Redo()
		{
			if (_redoStack.Count > 0)
			{
				var redo = _redoStack.Pop();
				foreach (var painting in redo)
				{
					SetTile(painting.X, painting.Y, painting.NewTile);
				}

				_undoStack.Push(redo);

				RecompressMap(redo);
			}
		}

		private void EditTriggersButton_Click(object sender, RoutedEventArgs e)
		{
			new WorldMapTriggersWindow(_rom).ShowDialog();
		}

		private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
		{
			MapScrollViewer.ScrollToHorizontalOffset(Math.Round(MapScrollViewer.HorizontalOffset));
			MapScrollViewer.ScrollToVerticalOffset(Math.Round(MapScrollViewer.VerticalOffset));
		}
	}
}
