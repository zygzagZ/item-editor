using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Drawing.Text;
using PluginInterface;
using ImageSimilarity;

namespace otitemeditor
{
	public partial class otitemeditor : Form
	{
		const string versionString = "otitemeditor 0.5.2";

		bool showOnlyMissMatchedItems = false;
		private TextBoxTraceListener textBoxListener;
		private bool showUpdateOutput = true;
		private bool showOtbOutput = false;

		private OtbList items = new OtbList();
		private OtbItem currentItem = null;

		//The plugin that is used to compare, sync and display sprite/dat data
		public Host.Types.Plugin currentPlugin;
		public UInt32 currentOtbVersion = 0;
		string currentOtbFullPath = "";

		//The original plugin that was used to open the currently loaded OTB
		public Host.Types.Plugin previousPlugin;

		public otitemeditor()
		{
			InitializeComponent();
		}

		private void otitemeditor_Load(object sender, EventArgs e)
		{
			this.Text = versionString;
			typeCombo.DataSource = Enum.GetNames(typeof(ItemType)); 

			Trace.Listeners.Clear();
			textBoxListener = new TextBoxTraceListener(outputTextBox);
			Trace.Listeners.Add(textBoxListener);

			updateTraceToolStripMenuItem.Checked = showUpdateOutput;
			otbTraceToolStripMenuItem.Checked = showOtbOutput;
			onSelectItem(null);

			Program.plugins.FindPlugins();
		}

		private Bitmap getBitmap(SpriteItem spriteItem)
		{
			int Width = 32;
			int Height = 32;

			if (spriteItem.width > 1 || spriteItem.height > 1)
			{
				Width = 64;
				Height = 64;
			}

			Bitmap canvas = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
			Graphics g = Graphics.FromImage(canvas);

			//draw sprite
			for (int frame = 0; frame < spriteItem.frames; frame++)
			{
				for (int cy = 0; cy < spriteItem.height; ++cy)
				{
					for (int cx = 0; cx < spriteItem.width; ++cx)
					{
						int frameIndex = cx + cy * spriteItem.width + frame * spriteItem.width * spriteItem.height;
						Bitmap bmp = ImageUtils.getBitmap(spriteItem.getRGBData(frameIndex), PixelFormat.Format32bppArgb, 32, 32);

						if (canvas.Width == 32)
						{
							g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
						}
						else
						{
							g.DrawImage(bmp, new Rectangle(Math.Max(32 - cx * 32, 0), Math.Max(32 - cy * 32, 0), bmp.Width, bmp.Height));
						}
					}
				}
			}

			g.Save();
			return canvas;
		}

		private void drawSprite(ref Bitmap canvas, SpriteItem spriteItem)
		{
			Graphics g = Graphics.FromImage(canvas);

			//draw sprite
			for (int frame = 0; frame < spriteItem.frames; frame++)
			{
				for (int cy = 0; cy < spriteItem.height; ++cy)
				{
					for (int cx = 0; cx < spriteItem.width; ++cx)
					{
						int frameIndex = cx + cy * spriteItem.width + frame * spriteItem.width * spriteItem.height;
						Bitmap bmp = ImageUtils.getBitmap(spriteItem.getRGBData(frameIndex), PixelFormat.Format32bppArgb, 32, 32);

						if (canvas.Width == 32)
						{
							g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
						}
						else
						{
							g.DrawImage(bmp, new Rectangle(Math.Max(32 - cx * 32, 0), Math.Max(32 - cy * 32, 0), bmp.Width, bmp.Height));
						}
					}
				}
			}

			g.Save();
		}

		private void drawSprite(PictureBox picturBox, SpriteItem spriteItem)
		{
			Bitmap canvas = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(canvas))
			{
				g.FillRectangle(new SolidBrush(Color.FromArgb(0x11, 0x11, 0x11)), 0, 0, canvas.Width, canvas.Height);
				g.Save();
			}

			drawSprite(ref canvas, spriteItem);

			Bitmap newImage = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(newImage))
			{
				g.DrawImage(canvas, new Point((canvas.Width > 32 ? 0 : 32), (canvas.Height > 32 ? 0 : 32)));
				g.Save();
			}

			newImage.MakeTransparent(Color.FromArgb(0x11, 0x11, 0x11));
			picturBox.Image = newImage;
		}
		
		private void buildTreeView()
		{
			clearTreeView();
			TreeNode groundNode = itemTreeView.Nodes.Add("Ground");
			TreeNode containerNode = itemTreeView.Nodes.Add("Container");
			TreeNode fluidNode = itemTreeView.Nodes.Add("Fluid");
			TreeNode splashNode = itemTreeView.Nodes.Add("Splash");
			TreeNode otherNode = itemTreeView.Nodes.Add("Other");
			TreeNode deprecatedNode = itemTreeView.Nodes.Add("Deprecated");

			foreach (OtbItem item in items)
			{
				if (showOnlyMissMatchedItems && compareItem(item, true))
				{
					continue;
				}

				switch (item.type)
				{
					case ItemType.Ground:
						{
							TreeNode node = groundNode.Nodes.Add(string.Format("{0}", item.id));
							node.Tag = item;
						} break;

					case ItemType.Container:
						{
							TreeNode node = containerNode.Nodes.Add(string.Format("{0}", item.id));
							node.Tag = item;
						} break;

					case ItemType.Fluid:
						{
							TreeNode node = fluidNode.Nodes.Add(string.Format("{0}", item.id));
							node.Tag = item;
						} break;

					case ItemType.Splash:
						{
							TreeNode node = splashNode.Nodes.Add(string.Format("{0}", item.id));
							node.Tag = item;
						} break;

					case ItemType.Deprecated:
						{
							TreeNode node = deprecatedNode.Nodes.Add(string.Format("{0}", item.id));
							node.Tag = item;
						} break;

					default:
						{
							TreeNode node = otherNode.Nodes.Add(string.Format("{0}", item.id));
							node.Tag = item;
						} break;
				}
			}
		}

		private void clearTreeView()
		{
			itemTreeView.Nodes.Clear();
		}

		private bool compareItem(OtbItem item, bool compareHash)
		{
			if (item.type == ItemType.Deprecated)
			{
				return true;
			}

			SpriteItem spriteItem;
			if (currentPlugin.Instance.Items.TryGetValue(item.spriteId, out spriteItem))
			{
				if (compareHash && !Utils.ByteArrayCompare(item.spriteHash, spriteItem.spriteHash))
				{
					return false;
				}

				return item.isEqual(spriteItem);
			}

			return false;
		}

		private void reloadItems()
		{
			foreach (OtbItem item in items)
			{
				if (!compareItem(item, true))
				{
					reloadItem(item);
				}
			}
		}

		private void reloadItem(OtbItem item)
		{
			//to avoid problems with events
			OtbItem tmpItem = currentItem;
			currentItem = null;

			SpriteItem spriteItem;
			if (currentPlugin.Instance.Items.TryGetValue(item.spriteId, out spriteItem))
			{
				if (showUpdateOutput)
				{
					Trace.WriteLine(String.Format("Reloading item id: {0}", item.id));
				}

				UInt16 tmpId = item.id;
				item.itemImpl = (ItemImpl)spriteItem.itemImpl.Clone();
				item.id = tmpId;
				Buffer.BlockCopy(spriteItem.spriteHash, 0, item.spriteHash, 0, spriteItem.spriteHash.Length);

				currentItem = tmpItem;
			}
		}
		
		private bool showItem(OtbItem item)
		{
			currentItem = null;
			resetAllDatabindings(this);

			if (item == null)
			{
				return false;
			}

			SpriteItem spriteItem;
			ushort spriteId = item.spriteId;
			if (spriteId == 0)
				spriteId = 100;

			if (!currentPlugin.Instance.Items.TryGetValue(spriteId, out spriteItem))
			{
				return false;
			}

			duplicateItemToolStripMenuItem.Enabled = true;

			drawSprite(pictureBox, spriteItem);
			if (!item.isCustomCreated && item.spriteHash != null && spriteItem.spriteHash != null)
			{
				pictureBox.BackColor = ((Utils.ByteArrayCompare(item.spriteHash, spriteItem.spriteHash) ? Color.White : Color.Red));
			}

			typeCombo.Text = item.type.ToString();
			typeCombo.ForeColor = (item.type == spriteItem.type ? Color.Black : Color.Red);

			//
			serverIdLbl.DataBindings.Add("Text", item, "id");
			clientIdUpDown.Minimum = items.minId;
			clientIdUpDown.Maximum = items.maxId;
			clientIdUpDown.DataBindings.Add("Value", spriteItem, "id");

			//Options
			blockObjectCheck.DataBindings.Add("Checked", item, "blockObject");
			blockObjectCheck.ForeColor = (item.blockObject == spriteItem.blockObject ? Color.Black : Color.Red);

			blockGhostsCheck.DataBindings.Add("Checked", item, "blockGhosts");
			blockGhostsCheck.ForeColor = Color.Black;

			blockProjectileCheck.DataBindings.Add("Checked", item, "blockProjectile");
			blockProjectileCheck.ForeColor = (item.blockProjectile == spriteItem.blockProjectile ? Color.Black : Color.Red);

			blockPathFindCheck.DataBindings.Add("Checked", item, "blockPathFind");
			blockPathFindCheck.ForeColor = (item.blockPathFind == spriteItem.blockPathFind ? Color.Black : Color.Red);

			moveableCheck.DataBindings.Add("Checked", item, "isMoveable");
			moveableCheck.ForeColor = (item.isMoveable == spriteItem.isMoveable ? Color.Black : Color.Red);

			hasHeightCheck.DataBindings.Add("Checked", item, "hasHeight");
			hasHeightCheck.ForeColor = (item.hasHeight == spriteItem.hasHeight ? Color.Black : Color.Red);

			pickupableCheck.DataBindings.Add("Checked", item, "isPickupable");
			pickupableCheck.ForeColor = (item.isPickupable == spriteItem.isPickupable ? Color.Black : Color.Red);

			hangableCheck.DataBindings.Add("Checked", item, "isHangable");
			hangableCheck.ForeColor = (item.isHangable == spriteItem.isHangable ? Color.Black : Color.Red);

			useableCheck.DataBindings.Add("Checked", item, "hasUseWith");
			useableCheck.ForeColor = (item.hasUseWith == spriteItem.hasUseWith ? Color.Black : Color.Red);

			rotatableCheck.DataBindings.Add("Checked", item, "isRotatable");
			rotatableCheck.ForeColor = (item.isRotatable == spriteItem.isRotatable ? Color.Black : Color.Red);

			stackableCheck.DataBindings.Add("Checked", item, "isStackable");
			stackableCheck.ForeColor = (item.isStackable == spriteItem.isStackable ? Color.Black : Color.Red);

			verticalCheck.DataBindings.Add("Checked", item, "isVertical");
			verticalCheck.ForeColor = (item.isVertical == spriteItem.isVertical ? Color.Black : Color.Red);

			walkStackCheck.DataBindings.Add("Checked", item, "walkStack");
			walkStackCheck.ForeColor = (item.walkStack == spriteItem.walkStack ? Color.Black : Color.Red);

			horizontalCheck.DataBindings.Add("Checked", item, "isHorizontal");
			horizontalCheck.ForeColor = (item.isHorizontal == spriteItem.isHorizontal ? Color.Black : Color.Red);

			alwaysOnTopCheck.DataBindings.Add("Checked", item, "alwaysOnTop");
			alwaysOnTopCheck.ForeColor = (item.alwaysOnTop == spriteItem.alwaysOnTop ? Color.Black : Color.Red);

			readableCheck.DataBindings.Add("Checked", item, "isReadable");
			readableCheck.ForeColor = (item.isReadable == spriteItem.isReadable ? Color.Black : Color.Red);

			speedText.DataBindings.Add("Text", item, "groundSpeed");
			speedText.ForeColor = (item.groundSpeed == spriteItem.groundSpeed ? Color.Black : Color.Red);

			topOrderText.DataBindings.Add("Text", item, "alwaysOnTopOrder");
			topOrderText.ForeColor = (item.alwaysOnTopOrder == spriteItem.alwaysOnTopOrder ? Color.Black : Color.Red);

			lightLevelText.DataBindings.Add("Text", item, "lightLevel");
			lightLevelText.ForeColor = (item.lightLevel == spriteItem.lightLevel ? Color.Black : Color.Red);

			lightColorText.DataBindings.Add("Text", item, "lightColor");
			lightColorText.ForeColor = (item.lightColor == spriteItem.lightColor ? Color.Black : Color.Red);

			maxReadCharsText.DataBindings.Add("Text", item, "maxReadChars");
			maxReadCharsText.ForeColor = (item.maxReadChars == spriteItem.maxReadChars ? Color.Black : Color.Red);

			maxReadWriteCharsText.DataBindings.Add("Text", item, "maxReadWriteChars");
			maxReadWriteCharsText.ForeColor = (item.maxReadWriteChars == spriteItem.maxReadWriteChars ? Color.Black : Color.Red);

			lookThroughCheck.DataBindings.Add("Checked", item, "lookThrough");
			lookThroughCheck.ForeColor = (item.lookThrough == spriteItem.lookThrough ? Color.Black : Color.Red);

			minimapColorText.DataBindings.Add("Text", item, "minimapColor");
			minimapColorText.ForeColor = (item.minimapColor == spriteItem.minimapColor ? Color.Black : Color.Red);

			wareIdText.DataBindings.Add("Text", item, "wareId");
			wareIdText.ForeColor = (item.wareId == spriteItem.wareId ? Color.Black : Color.Red);

			nameText.DataBindings.Add("Text", item, "name");
			nameText.ForeColor = (item.name.CompareTo(spriteItem.name) == 0 ? Color.Black : Color.Red);

			tableLayoutPanelCandidates.Visible = false;
			for (int i = 0; i < tableLayoutPanelCandidates.ColumnCount; ++i)
			{
				PictureBox box = (PictureBox)tableLayoutPanelCandidates.GetControlFromPosition(i, 0);
				box.Image = null;
			}

			if (previousPlugin != null)
			{
				SpriteItem prevSpriteItem;
				if (previousPlugin.Instance.Items.TryGetValue(item.prevSpriteId, out prevSpriteItem))
				{
					drawSprite(prevPictureBox, prevSpriteItem);

					if (prevSpriteItem.spriteSignature != null)
					{
						//Sprite does not match, use the sprite signature to find possible candidates
						showSpriteCandidates(prevSpriteItem);
					}
				}
				else
				{
					prevPictureBox.Image = null;
				}
			}

			currentItem = item;
			return true;
		}

		private UInt16 createItem(Item item)
		{
			UInt16 newId = (UInt16)(items.maxId + 1);

			//create a new otb item
			OtbItem newItem = new OtbItem(item);
			newItem.id = newId;
			newItem.spriteHash = new byte[16];

			if (item != null)
			{
				newItem.spriteId = item.id;
				Buffer.BlockCopy(item.spriteHash, 0, newItem.spriteHash, 0, newItem.spriteHash.Length);
			}
			else
			{
				newItem.spriteId = items.minId;
				newItem.isCustomCreated = true;
			}

			items.Add(newItem);
			return newId;
		}

		private void removeLastItem()
		{
			items.RemoveAt(items.Count - 1);
		}

		private void showSpriteCandidates(SpriteItem spriteItem)
		{
			tableLayoutPanelCandidates.Visible = true;

			//list with the top 5 results
			List<KeyValuePair<double, OtbItem>> signatureList = new List<KeyValuePair<double, OtbItem>>();

			foreach (OtbItem cmpItem in items)
			{
				if (cmpItem.type == ItemType.Deprecated)
				{
					continue;
				}

				SpriteItem cmpSpriteItem;
				if(!currentPlugin.Instance.Items.TryGetValue(cmpItem.spriteId, out cmpSpriteItem))
				{
					continue;
				}

				double similarity = ImageUtils.CompareSignature(spriteItem.spriteSignature, cmpSpriteItem.spriteSignature);

				foreach (KeyValuePair<double, OtbItem> kvp in signatureList)
				{
					if (similarity < kvp.Key)
					{
						//TODO: Use isEqual aswell to match against attributes.
						signatureList.Remove(kvp);
						break;
					}
				}

				if (signatureList.Count < 5)
				{
					KeyValuePair<double, OtbItem> kvp = new KeyValuePair<double, OtbItem>(similarity, cmpItem);
					signatureList.Add(kvp);
				}
			}

			signatureList.Sort(
				delegate(KeyValuePair<double, OtbItem> item1, KeyValuePair<double, OtbItem> item2)
				{
					return item1.Key.CompareTo(item2.Key);
				});

			//those with lowest value are the closest match
			int index = 0;
			foreach (KeyValuePair<double, OtbItem> kvp in signatureList)
			{
				PictureBox box = (PictureBox)tableLayoutPanelCandidates.GetControlFromPosition(index, 0);
				box.Tag = kvp.Value;

				SpriteItem spriteCandidateItem;
				if (currentPlugin.Instance.Items.TryGetValue(kvp.Value.spriteId, out spriteCandidateItem))
				{
					drawSprite(box, spriteCandidateItem);
				}
				++index;
			}
		}

		public bool loadClient(Host.Types.Plugin plugin, UInt32 otbVersion)
		{
			SupportedClient client = plugin.Instance.SupportedClients.Find(
				delegate(SupportedClient sc)
				{
					return sc.otbVersion == otbVersion;
				});

			if (client == null)
			{
				MessageBox.Show("The selected plugin does not support this version.");
				return false;
			}

			string dataFolder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "data");

			if (Directory.Exists(dataFolder) == false)
			{
				Directory.CreateDirectory(dataFolder);
			}

			string datPath = FindClientFile(Path.Combine(dataFolder, client.version.ToString()), ".dat");
			string sprPath = FindClientFile(Path.Combine(dataFolder, client.version.ToString()), ".spr");

			if (File.Exists(datPath) == false || File.Exists(sprPath) == false)
			{
				string text = String.Empty;

				if (File.Exists(datPath) == false)
				{
					text = String.Format("Unable to load dat file, please place a valid dat in 'data\\{0}\\'.", client.version);
				}

				if (File.Exists(sprPath) == false)
				{
					if (text != String.Empty)
					{
						text += "\n";
					}
					text += String.Format("Unable to load spr file, please place a valid spr in 'data\\{0}\\'.", client.version);
				}

				MessageBox.Show(text);
				return false;
			}

			bool result = plugin.Instance.LoadClient(client, datPath, sprPath);
			if (!result)
			{
				MessageBox.Show(String.Format("The plugin could not load dat or spr."));
			}

			items.clientVersion = client.version;
			return result;
		}

		private string FindClientFile(string Path, string extension)
		{
			try
			{
				foreach (string fileOn in Directory.GetFiles(Path))
				{
					FileInfo file = new FileInfo(fileOn);
					if (file.Extension.Equals(extension))
					{
						return file.FullName;
					}
				}
			}
			catch (Exception e)
			{
				return String.Empty;
			}
			return String.Empty;
		}

		private bool generateSpriteSignatures(ref SpriteItems items)
		{
			if (items.signatureCalculated)
			{
				return true;
			}

			ProgressForm progress = new ProgressForm();
			progress.StartPosition = FormStartPosition.Manual;
			progress.Location = new Point(Location.X + ((Width - progress.Width) / 2),
				Location.Y + ((Height - progress.Height) / 2));
			progress.bar.Minimum = 0;
			progress.bar.Maximum = items.Count;
			progress.Show(this);

			foreach (SpriteItem spriteItem in items.Values)
			{
				Bitmap spriteBmp = getBitmap(spriteItem);
				Bitmap ff2dBmp = Fourier.fft2dRGB(spriteBmp, false);
				spriteItem.spriteSignature = ImageUtils.CalculateEuclideanDistance(ff2dBmp, 1);

				if (progress.bar.Value % 20 == 0)
				{
					Application.DoEvents();
				}
				progress.progressLbl.Text = String.Format(
					"Calculating image signature for {0}", spriteItem.id);
				++progress.bar.Value;
			}

			items.signatureCalculated = true;
			progress.Close();
			return true;
		}

		private void resetAllDatabindings(Control control)
		{
			control.DataBindings.Clear();
			if (control.HasChildren)
			{
				foreach (Control childControl in control.Controls)
				{
					resetAllDatabindings(childControl);
				}
			}
		}

		private void onSelectItem(TreeNode node)
		{
			if (node != null && node.Tag != null)
			{
				OtbItem item = (OtbItem)node.Tag;
				showItem(item);
			}
			else
			{
				currentItem = null;

				pictureBox.Image = null;
				pictureBox.BackColor = Color.White;
				prevPictureBox.Image = null;
				prevPictureBox.BackColor = Color.White;
				tableLayoutPanelCandidates.Visible = false;
				clientIdUpDown.Value = clientIdUpDown.Minimum;
				serverIdLbl.Text = "0";
				typeCombo.Text = "";
				typeCombo.ForeColor = Color.Black;
				duplicateItemToolStripMenuItem.Enabled = false;

				foreach (Control control in optionGroupBox.Controls)
				{
					if (control is CheckBox)
					{
						((CheckBox)control).Checked = false;
						control.ForeColor = Color.Black;
					}
					else if (control is TextBox)
					{
						((TextBox)control).Text = "";
						control.ForeColor = Color.Black;
					}
				}
			}
		}

		private void itemTreeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
		{
			if (currentItem != null && currentItem.isCustomCreated)
			{
				//Making sure the hash is updated
				SpriteItem spriteItem;
				if (currentPlugin.Instance.Items.TryGetValue(currentItem.spriteId, out spriteItem))
				{
					Buffer.BlockCopy(spriteItem.spriteHash, 0, currentItem.spriteHash, 0, spriteItem.spriteHash.Length);
				}
			}
			
			currentItem = null;
		}

		private void itemTreeView_AfterSelect(object sender, TreeViewEventArgs e)
		{
			onSelectItem(itemTreeView.SelectedNode);
		}

		private void compareOtbToolStripMenuItem_Click(object sender, EventArgs e)
		{
			CompareOtbForm form = new CompareOtbForm();
			form.ShowDialog();
		}

		private void openToolStripMenuItem_Click(object sender, EventArgs e)
		{
			FileDialog dialog = new OpenFileDialog();

			//Now set the file type
			dialog.Filter = "otb files (*.otb)|*.otb|All files (*.*)|*.*";
			dialog.Title = "Open otb file...";

			if (dialog.ShowDialog() != DialogResult.OK || dialog.FileName.Length == 0)
			{
				return;
			}

			currentItem = null;
			currentPlugin = null;
			previousPlugin = null;
			currentOtbVersion = 0;

			items.Clear();

			if (otb.open(dialog.FileName, ref items, showOtbOutput))
			{
				currentOtbFullPath = dialog.FileName;
				currentOtbVersion = items.dwMinorVersion;

				saveAsToolStripMenuItem.Enabled = true;
				saveToolStripMenuItem.Enabled = true;

				//try find a plugin that can handle this version of otb
				currentPlugin = Program.plugins.AvailablePlugins.Find(currentOtbVersion);
				if (currentPlugin == null)
				{
					items.Clear();
					MessageBox.Show(String.Format("Could not find a plugin that could handle client version {0}", currentOtbVersion));
					return;
				}

				if (!loadClient(currentPlugin, currentOtbVersion))
				{
					currentPlugin = null;
					items.Clear();
					return;
				}

				createItemToolStripMenuItem.Enabled = true;
				updateToolStripMenuItem.Enabled = true;
				buildTreeView();
			}
		}

		private void saveToolStripMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				otb.save(currentOtbFullPath, ref items);
			}
			catch (UnauthorizedAccessException exception)
			{
				MessageBox.Show(exception.Message);
			}
		}

		private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			SaveFileDialog dialog = new SaveFileDialog();
			//Now set the file type
			dialog.Filter = "otb files (*.otb)|*.otb|All files (*.*)|*.*";

			dialog.Title = "Save otb file...";

			if (dialog.ShowDialog() == DialogResult.OK)
			{
				if (dialog.FileName.Length == 0)
				{
					return;
				}

				try
				{
					otb.save(dialog.FileName, ref items);
					currentOtbFullPath = dialog.FileName;
				}
				catch (UnauthorizedAccessException exception)
				{
					MessageBox.Show(exception.Message);
				}
			}
		}

		private void showOnlyUnmatchedToolStripMenuItem_Click(object sender, EventArgs e)
		{
			showOnlyMissMatchedItems = !showOnlyMissMatchedItems;
			buildTreeView();
		}

		private void updateTraceToolStripMenuItem_Click(object sender, EventArgs e)
		{
			showUpdateOutput = !showUpdateOutput;
		}

		private void otbTraceToolStripMenuItem_Click(object sender, EventArgs e)
		{
			showOtbOutput = !showOtbOutput;
		}

		private void reloadItemAttributesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			reloadItems();
			showItem(currentItem);
			buildTreeView();
		}

		private void updateToolStripMenuItem_Click(object sender, EventArgs e)
		{
			UpdateForm form = new UpdateForm();
			form.mainForm = this;

			DialogResult result = form.ShowDialog();
			if (result == DialogResult.OK)
			{
				//Update OTB
				Host.Types.Plugin updatePlugin = form.selectedPlugin;
				SupportedClient updateClient = form.updateClient;

				if (updatePlugin == null)
				{
					return;
				}

				if (!loadClient(updatePlugin, updateClient.otbVersion))
				{
					return;
				}

				UpdateSettingsForm updateSettingsForm = new UpdateSettingsForm();
				result = updateSettingsForm.ShowDialog();
				if (result != DialogResult.OK)
				{
					return;
				}

				if (updateSettingsForm.generateSignatureCheck.Checked)
				{
					//Calculate an image signature using fourier transformation and calculate a signature we can
					//use to compare it to other images (kinda similar to md5 hash) except this
					//can also be used to find images with some variation.
					SpriteItems currentSpriteItems = currentPlugin.Instance.Items;
					generateSpriteSignatures(ref currentSpriteItems);

					SpriteItems updateSpriteItems = updatePlugin.Instance.Items;
					generateSpriteSignatures(ref updateSpriteItems);
				}

				SpriteItems currentItems = currentPlugin.Instance.Items;
				SpriteItems updateItems = updatePlugin.Instance.Items;
				List<UInt16> assignedSpriteIdList = new List<UInt16>();

				//store the previous plugin (so we can display previous sprite, and do other comparisions)
				previousPlugin = currentPlugin;

				//update the current plugin the one we are updating to
				currentPlugin = updatePlugin;

				//update version information
				items.clientVersion = updateClient.version;
				items.dwMinorVersion = updateClient.otbVersion;
				items.dwBuildNumber = items.dwBuildNumber + 1;
				currentOtbVersion = items.dwMinorVersion;

				//Most items does have the same sprite after an update, so lets try that first
				UInt32 foundItemCounter = 0;
				foreach (OtbItem item in items)
				{
					item.spriteAssigned = false;

					if (item.type == ItemType.Deprecated)
					{
						continue;
					}

					SpriteItem updateSpriteItem;
					if (updateItems.TryGetValue(item.spriteId, out updateSpriteItem))
					{
						bool compareResult = updateSpriteItem.isEqual(item);

						if (Utils.ByteArrayCompare(updateSpriteItem.spriteHash, item.spriteHash))
						{
							if (compareResult)
							{
								item.prevSpriteId = item.spriteId;
								item.spriteId = updateSpriteItem.id;
								item.spriteAssigned = true;

								assignedSpriteIdList.Add(updateSpriteItem.id);
								++foundItemCounter;

								if (showUpdateOutput)
								{
									//Trace.WriteLine(String.Format("Match found id: {0}, clientid: {1}", item.otb.id, item.dat.id));
								}
							}
							else
							{
								//Sprite matches, but not the other attributes.
								if (showUpdateOutput)
								{
									Trace.WriteLine(String.Format("Attribute changes found id: {0}", item.id));
								}
							}
						}
					}
				}

				if (updateSettingsForm.reassignUnmatchedSpritesCheck.Checked)
				{
					foreach (Item updateItem in updateItems.Values)
					{
						foreach (OtbItem item in items)
						{
							if (item.type == ItemType.Deprecated)
							{
								continue;
							}

							if (item.spriteAssigned)
							{
								continue;
							}

							if (Utils.ByteArrayCompare(updateItem.spriteHash, item.spriteHash))
							{
								if (updateItem.isEqual(item))
								{
									if (updateItem.id != item.spriteId)
									{
										if (showUpdateOutput)
										{
											Trace.WriteLine(String.Format("New sprite found id: {0}, old: {1}, new: {2}", item.id, item.spriteId, updateItem.id));
										}
									}

									item.prevSpriteId = item.spriteId;
									item.spriteId = updateItem.id;
									item.spriteAssigned = true;

									assignedSpriteIdList.Add(updateItem.id);
									++foundItemCounter;
									break;
								}
							}
						}
					}
				}

				if (showUpdateOutput)
				{
					Trace.WriteLine(String.Format("Found {0} of {1}", foundItemCounter, items.maxId));
				}

				if (updateSettingsForm.reloadItemAttributesCheck.Checked)
				{
					UInt32 reloadedItemCounter = 0;
					foreach (OtbItem item in items)
					{
						if (item.type == ItemType.Deprecated)
						{
							continue;
						}

						//implicit assigned
						item.prevSpriteId = item.spriteId;
						item.spriteAssigned = true;

						if (!assignedSpriteIdList.Contains(item.spriteId))
						{
							assignedSpriteIdList.Add(item.spriteId);
						}

						if (!compareItem(item, true))
						{
							//sync with dat info
							reloadItem(item);
							++reloadedItemCounter;
						}
					}

					if (showUpdateOutput)
					{
						Trace.WriteLine(String.Format("Reloaded {0} of {1}", reloadedItemCounter, items.maxId));
					}
				}

				if (updateSettingsForm.createNewItemsCheck.Checked)
				{
					UInt32 newItemCounter = 0;
					foreach (Item updateItem in updateItems.Values)
					{
						if (!assignedSpriteIdList.Contains(updateItem.id))
						{
							++newItemCounter;

							UInt16 newId = createItem(updateItem);

							if (showUpdateOutput)
							{
								Trace.WriteLine(String.Format("Creating item id {0}", newId));
							}
						}
					}

					if (showUpdateOutput)
					{
						Trace.WriteLine(String.Format("Created {0} new items", newItemCounter));
					}
				}

				//done
				buildTreeView();
			}
		}

		private void pictureBoxCandidate_Click(object sender, EventArgs e)
		{
			if (currentItem != null)
			{
				PictureBox box = (PictureBox)sender;
				if (box.Tag is OtbItem)
				{
					OtbItem newItem = (OtbItem)box.Tag;

					SpriteItem spriteItem;
					if (!currentPlugin.Instance.Items.TryGetValue(newItem.spriteId, out spriteItem))
					{
						return;
					}

					if (!spriteItem.isEqual(currentItem))
					{
						DialogResult result = MessageBox.Show(
							"The item attributes does not match the current information, would you like to continue anyway?",
							"Item attributes does not match",
							MessageBoxButtons.YesNo,
							MessageBoxIcon.Question,
							MessageBoxDefaultButton.Button1);

						if (result != DialogResult.Yes)
						{
							return;
						}
					}

					currentItem.prevSpriteId = currentItem.spriteId;
					currentItem.spriteId = spriteItem.id;
					showItem(currentItem);
				}
			}
		}

		private void clientIdUpDown_ValueChanged(object sender, EventArgs e)
		{
			if (currentItem != null)
			{
				SpriteItem newSpriteItem;
				if (currentPlugin.Instance.Items.TryGetValue((UInt16)clientIdUpDown.Value, out newSpriteItem))
				{
					currentItem.prevSpriteId = currentItem.spriteId;
					currentItem.spriteId = newSpriteItem.id;
					showItem(currentItem);
				}
			}
		}

		private void updateItem(object sender, EventArgs e)
		{
			if (currentItem != null)
			{
				Control c = (Control) sender;
				var d = new Dictionary<Control, string>();
				d[typeCombo] = "type";
				d[blockObjectCheck] = "blockObject";
				d[blockProjectileCheck] = "blockProjectile";
				d[blockPathFindCheck] = "blockPathFind";
				d[moveableCheck] = "isMoveable";
				d[hasHeightCheck] = "hasHeight";
				d[pickupableCheck] = "isPickupable";
				d[hangableCheck] = "isHangable";
				d[useableCheck] = "hasUseWith";
				d[rotatableCheck] = "isRotatable";
				d[stackableCheck] = "isStackable";
				d[verticalCheck] = "isVertical";
				d[walkStackCheck] = "walkStack";
				d[horizontalCheck] = "isHorizontal";
				d[alwaysOnTopCheck] = "alwaysOnTop";
				d[readableCheck] = "isReadable";
				d[speedText] = "groundSpeed";
				d[topOrderText] = "alwaysOnTopOrder";
				d[lightLevelText] = "lightLevel";
				d[lightColorText] = "lightColor";
				d[maxReadCharsText] = "maxReadChars";
				d[maxReadWriteCharsText] = "maxReadWriteChars";
				d[lookThroughCheck] = "lookThrough";
				d[minimapColorText] = "minimapColor";
				d[wareIdText] = "wareId";
				d[nameText] = "name";

				SpriteItem spriteItem;
				ushort spriteId = currentItem.spriteId;
				if (spriteId == 0)
					spriteId = 100;

				if (!currentPlugin.Instance.Items.TryGetValue(spriteId, out spriteItem))
				{
					return;
				}
				if (!currentItem.isCustomCreated && currentItem.spriteHash != null && spriteItem.spriteHash != null)
				{
					pictureBox.BackColor = ((Utils.ByteArrayCompare(currentItem.spriteHash, spriteItem.spriteHash) ? Color.White : Color.Red));
				}
				if (c is CheckBox) {
					c.ForeColor = (((CheckBox)c).Checked == (bool)spriteItem.GetType().GetProperty(d[c]).GetValue(spriteItem, null) ? Color.Black : Color.Red);
				}
				speedText.ForeColor = (currentItem.groundSpeed == spriteItem.groundSpeed ? Color.Black : Color.Red);
				topOrderText.ForeColor = (currentItem.alwaysOnTopOrder == spriteItem.alwaysOnTopOrder ? Color.Black : Color.Red);
				lightLevelText.ForeColor = (currentItem.lightLevel == spriteItem.lightLevel ? Color.Black : Color.Red);
				lightColorText.ForeColor = (currentItem.lightColor == spriteItem.lightColor ? Color.Black : Color.Red);
				maxReadCharsText.ForeColor = (currentItem.maxReadChars == spriteItem.maxReadChars ? Color.Black : Color.Red);
				maxReadWriteCharsText.ForeColor = (currentItem.maxReadWriteChars == spriteItem.maxReadWriteChars ? Color.Black : Color.Red);
				minimapColorText.ForeColor = (currentItem.minimapColor == spriteItem.minimapColor ? Color.Black : Color.Red);
				wareIdText.ForeColor = (currentItem.wareId == spriteItem.wareId ? Color.Black : Color.Red);
				nameText.ForeColor = (currentItem.name.CompareTo(spriteItem.name) == 0 ? Color.Black : Color.Red);
			}
		}

		private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
		{
			MessageBox.Show("Information how to use this program check http://www.otfans.net.",
				"About " + versionString, 
				MessageBoxButtons.OK,
				MessageBoxIcon.None,
				MessageBoxDefaultButton.Button1,
				0);
		}

		private void copySpriteButton_Click(object sender, EventArgs e)
		{
			if (currentItem != null) {
				SpriteItem spriteItem;
				ushort spriteId = currentItem.spriteId;
				if (spriteId == 0)
					spriteId = 100;

				if (!currentPlugin.Instance.Items.TryGetValue(spriteId, out spriteItem))
				{
					return;
				}
				currentItem.blockObject = spriteItem.blockObject;
				currentItem.blockProjectile = spriteItem.blockProjectile;
				currentItem.blockPathFind = spriteItem.blockPathFind;
				currentItem.isMoveable = spriteItem.isMoveable;
				currentItem.hasHeight = spriteItem.hasHeight;
				currentItem.isPickupable = spriteItem.isPickupable;
				currentItem.isHangable = spriteItem.isHangable;
				currentItem.hasUseWith = spriteItem.hasUseWith;
				currentItem.isRotatable = spriteItem.isRotatable;
				currentItem.isStackable = spriteItem.isStackable;
				currentItem.isVertical = spriteItem.isVertical;
				currentItem.walkStack = spriteItem.walkStack;
				currentItem.isHorizontal = spriteItem.isHorizontal;
				currentItem.alwaysOnTop = spriteItem.alwaysOnTop;
				currentItem.isReadable = spriteItem.isReadable;
				currentItem.groundSpeed = spriteItem.groundSpeed;
				currentItem.alwaysOnTopOrder = spriteItem.alwaysOnTopOrder;
				currentItem.lightLevel = spriteItem.lightLevel;
				currentItem.lightColor = spriteItem.lightColor;
				currentItem.maxReadChars = spriteItem.maxReadChars;
				currentItem.maxReadWriteChars = spriteItem.maxReadWriteChars;
				currentItem.lookThrough = spriteItem.lookThrough;
				currentItem.minimapColor = spriteItem.minimapColor;
				currentItem.wareId = spriteItem.wareId;
				currentItem.name = spriteItem.name;
				showItem(currentItem);
			}
		}

		private void duplicateItemToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (currentItem != null)
			{
				UInt16 newId = createItem(currentItem);
				Trace.WriteLine(String.Format("Creating item id {0}", newId));
			}
		}

		private void removeLastItemToolStripMenuItem_Click(object sender, EventArgs e)
		{
			removeLastItem();
		}

		private void createItemToolStripMenuItem_Click(object sender, EventArgs e)
		{
			UInt16 newId = createItem(null);
			Trace.WriteLine(String.Format("Creating item id {0}", newId));
		}

		private void typeCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (currentItem != null)
			{
				currentItem.type = (ItemType)Enum.Parse(typeof(ItemType), typeCombo.SelectedValue.ToString());
				Trace.WriteLine(String.Format("Item {0} type = {1}", currentItem.id, typeCombo.SelectedValue.ToString()));
			} else {
				Trace.WriteLine("currentItem == null");
			}
		}

		private void updateTreeviewToolStripMenuItem_Click(object sender, EventArgs e)
		{
			buildTreeView();
		}
	}
}
