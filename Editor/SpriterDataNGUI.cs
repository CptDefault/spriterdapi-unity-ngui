// NGUI import/export plugin
// SpriterDataNGUI.cs
// Spriter Data API - Unity
//  
// Authors:
//       Josh Montoute <josh@thinksquirrel.com>
//       Justin Whitfort <cptdefault@gmail.com>
// 
// Copyright (c) 2012 Thinksquirrel Software, LLC
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this 
// software and associated documentation files (the "Software"), to deal in the Software 
// without restriction, including without limitation the rights to use, copy, modify, 
// merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit 
// persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or 
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT 
// NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// NGUI is (c) by Tasharen Entertainment. Spriter is (c) by BrashMonkey.
//

// Some notes:
//
// This class extends SpriterDataUnity to provide hooks specific to NGUI.
// This was developed using NGUI Free. It is untested with the full version, but should work fine.


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrashMonkey.Spriter.Data;
using BrashMonkey.Spriter.Data.ObjectModel;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BrashMonkey.Spriter.DataPlugins.NGUI
{
	class SpriterDataNGUI : SpriterDataUnity
	{
		#region Menu items

		// Allow import of a Spriter file
		[MenuItem("Spriter/[NGUI]Create new character")]
		public static void CreateAtlas()
		{
			var data = new SpriterDataNGUI();

			string lastPath = EditorPrefs.GetString("SpriterNGUILastPath");
			if (String.IsNullOrEmpty(lastPath))
				lastPath = Application.dataPath;

			lastPath = EditorUtility.OpenFilePanel("Open Spriter File", lastPath, "scml");
			EditorPrefs.SetString("SpriterNGUILastPath", lastPath);

			if (!String.IsNullOrEmpty(lastPath))
			{
				data.LoadData(lastPath);
			}
		}

		// Convenience item for debugging
		// TODO: Remove before shipping
		[MenuItem("Spriter/[NGUI]Reimport Last")]
		public static void CreateLastAtlas()
		{
			var data = new SpriterDataNGUI();

			if (!String.IsNullOrEmpty(EditorPrefs.GetString("SpriterNGUILastPath")))
			{
				data.LoadData(EditorPrefs.GetString("SpriterNGUILastPath"));
			}
		}

		#endregion

		protected override string GetSaveFolder()
		{
			return NGUIEditorTools.GetSelectionFolder();
		}

		protected override void GetSpriteInfo(SpriterNGUIColorHelper helper, out Vector2 paddingTL, out Vector2 paddingBR, out Vector2 size)
		{
			var uiSprite = helper.GetComponent<UISprite>();

			UIAtlas.Sprite atlasSprite = uiSprite.atlas.GetSprite(uiSprite.spriteName);

			paddingTL = new Vector2(atlasSprite.paddingLeft * atlasSprite.outer.width, atlasSprite.paddingTop * atlasSprite.outer.height);
			paddingBR = new Vector2(atlasSprite.paddingRight * atlasSprite.outer.width, atlasSprite.paddingBottom * atlasSprite.outer.height);
			size = new Vector2(atlasSprite.outer.width, atlasSprite.outer.height);
		}

		protected override void AddSprite(ISpriterTimelineObject obj, GameObject go)
		{
			var uiSprite = go.AddComponent<UISprite>();
			uiSprite.atlas = UISettings.atlas;
			uiSprite.spriteName = GetSpriteName(obj.targetFile.name);
			uiSprite.pivot = UIWidget.Pivot.TopLeft;
		}

		#region Sprite atlas creation
		protected override void CreateSpriteAtlas()
		{
			// Create paths and names based on selection
			UISettings.atlasName = entity.name;
			string prefabPath = GetSaveFolder() + UISettings.atlasName + " Atlas.prefab";
			string matPath = GetSaveFolder() + UISettings.atlasName + " Atlas.mat";

			// Create the material
			Shader shader = Shader.Find("Unlit/Transparent Colored");
			var mat = new Material(shader);

			// Save the material
			AssetDatabase.CreateAsset(mat, matPath);
			AssetDatabase.Refresh();

			// Load the material so it's usable
			mat = AssetDatabase.LoadAssetAtPath(matPath, typeof(Material)) as Material;

			// Create the atlas
#if UNITY_3_4
			Object prefab = EditorUtility.CreateEmptyPrefab(prefabPath); 
#else
			Object prefab = PrefabUtility.CreateEmptyPrefab(prefabPath);
#endif
			// Create a new game object for the atlas
			var go = new GameObject(UISettings.atlasName);

			go.AddComponent<UIAtlas>().spriteMaterial = mat;

			// Update the prefab
#if UNITY_3_4
			EditorUtility.ReplacePrefab(go, prefab);
#else
			PrefabUtility.ReplacePrefab(go, prefab);
#endif
			Object.DestroyImmediate(go);
			AssetDatabase.Refresh();

			// Select the atlas
			go = (GameObject)AssetDatabase.LoadAssetAtPath(prefabPath, typeof(GameObject));
			UISettings.atlas = go.GetComponent<UIAtlas>();

			// Textures
			var texLoad =
				files.Where(file => file.type == FileType.Image || file.type == FileType.Unknown)
					 .Select(file => Resources.LoadAssetAtPath(rootLocation + "/" + file.name, typeof(Texture)) as Texture)
					 .ToList();

			// Update the atlas
			UpdateAtlas(texLoad);
		}

		static void UpdateAtlas(List<Texture> textures)
		{
			// Create a list of sprites using the collected textures
			List<SpriteEntry> sprites = CreateSprites(textures);

			UpdateAtlas(UISettings.atlas, sprites);
		}
		static List<SpriteEntry> CreateSprites(List<Texture> textures)
		{
			var list = new List<SpriteEntry>();

			foreach (Texture tex in textures)
			{
				Texture2D oldTex = NGUIEditorTools.ImportTexture(tex, true, false);
				if (oldTex == null) continue;

				Color32[] pixels = oldTex.GetPixels32();

				int xmin = oldTex.width;
				int xmax = 0;
				int ymin = oldTex.height;
				int ymax = 0;
				int oldWidth = oldTex.width;
				int oldHeight = oldTex.height;

				for (int y = 0, yw = oldHeight; y < yw; ++y)
				{
					for (int x = 0, xw = oldWidth; x < xw; ++x)
					{
						Color32 c = pixels[y * xw + x];

						if (c.a != 0)
						{
							if (y < ymin) ymin = y;
							if (y > ymax) ymax = y;
							if (x < xmin) xmin = x;
							if (x > xmax) xmax = x;
						}
					}
				}

				int newWidth = (xmax - xmin) + 1;
				int newHeight = (ymax - ymin) + 1;

				if (newWidth > 0 && newHeight > 0)
				{
					var sprite = new SpriteEntry {Rect = new Rect(0f, 0f, oldTex.width, oldTex.height)};

					if (newWidth == oldWidth && newHeight == oldHeight)
					{
						sprite.Tex = oldTex;
						sprite.TemporaryTexture = false;
					}
					else
					{
						var newPixels = new Color32[newWidth * newHeight];

						for (int y = 0; y < newHeight; ++y)
						{
							for (int x = 0; x < newWidth; ++x)
							{
								int newIndex = y * newWidth + x;
								int oldIndex = (ymin + y) * oldWidth + (xmin + x);
								newPixels[newIndex] = pixels[oldIndex];
							}
						}

						// Create a new texture
						sprite.TemporaryTexture = true;
						sprite.Tex = new Texture2D(newWidth, newHeight) {name = oldTex.name};
						sprite.Tex.SetPixels32(newPixels);
						sprite.Tex.Apply();

						// Remember the padding offset
						sprite.MinX = xmin;
						sprite.MaxX = oldWidth - newWidth - xmin;
						sprite.MinY = ymin;
						sprite.MaxY = oldHeight - newHeight - ymin;
					}
					list.Add(sprite);
				}
			}
			return list;
		}
		static void UpdateAtlas(UIAtlas atlas, List<SpriteEntry> sprites)
		{
			if (sprites.Count > 0)
			{
				// Combine all sprites into a single texture and save it
				UpdateTexture(atlas, sprites);

				// Replace the sprites within the atlas
				ReplaceSprites(atlas, sprites);

				// Release the temporary textures
				ReleaseSprites(sprites);
			}
			else
			{

				atlas.spriteList.Clear();
				string path = NGUIEditorTools.GetSaveableTexturePath(atlas);

				atlas.spriteMaterial.mainTexture = null;

				if (!String.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
			}
			EditorUtility.SetDirty(atlas.gameObject);
		}
		static void UpdateTexture(UIAtlas atlas, List<SpriteEntry> sprites)
		{
			// Get the texture for the atlas
			var tex = atlas.texture as Texture2D;
			string oldPath = (tex != null) ? AssetDatabase.GetAssetPath(tex.GetInstanceID()) : "";
			string newPath = NGUIEditorTools.GetSaveableTexturePath(atlas);

			if (tex == null || oldPath != newPath)
			{
				// Create a new texture for the atlas
				tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);

				// Pack the sprites into this texture
				PackTextures(tex, sprites);
				byte[] bytes = tex.EncodeToPNG();
				File.WriteAllBytes(newPath, bytes);

				// Load the texture we just saved as a Texture2D
				AssetDatabase.Refresh();
				tex = NGUIEditorTools.ImportTexture(newPath, false, true);

				// Update the atlas texture
				if (tex == null) Debug.LogError("Failed to load the created atlas saved as " + newPath);

				else atlas.spriteMaterial.mainTexture = tex;

			}
			else
			{
				// Make the atlas readable so we can save it
				tex = NGUIEditorTools.ImportTexture(oldPath, true, false);

				// Pack all sprites into atlas texture
				PackTextures(tex, sprites);
				byte[] bytes = tex.EncodeToPNG();
				File.WriteAllBytes(newPath, bytes);

				// Re-import the newly created texture, turning off the 'readable' flag
				AssetDatabase.Refresh();
				NGUIEditorTools.ImportTexture(newPath, false, false);
			}
		}
		static void ReplaceSprites(UIAtlas atlas, List<SpriteEntry> sprites)
		{
			// Get the list of sprites we'll be updating

			List<UIAtlas.Sprite> spriteList = atlas.spriteList;

			var kept = new List<UIAtlas.Sprite>();

			// The atlas must be in pixels
			atlas.coordinates = UIAtlas.Coordinates.Pixels;

			// Run through all the textures we added and add them as sprites to the atlas
			for (int i = 0; i < sprites.Count; ++i)
			{
				SpriteEntry se = sprites[i];
				UIAtlas.Sprite sprite = AddSprite(spriteList, se);
				kept.Add(sprite);
			}

			// Remove unused sprites
			for (int i = spriteList.Count; i > 0; )
			{
				UIAtlas.Sprite sp = spriteList[--i];
				if (!kept.Contains(sp)) spriteList.RemoveAt(i);
			}
			atlas.MarkAsDirty();
		}
		static void ReleaseSprites(IEnumerable<SpriteEntry> sprites)
		{
			foreach (SpriteEntry se in sprites.Where(se => se.TemporaryTexture))
			{
				NGUITools.Destroy(se.Tex);
				se.Tex = null;
			}
			Resources.UnloadUnusedAssets();
		}

		static void PackTextures(Texture2D tex, IList<SpriteEntry> sprites)
		{
			var textures = new Texture2D[sprites.Count];
			for (int i = 0; i < sprites.Count; ++i) textures[i] = sprites[i].Tex;

			Rect[] rects = tex.PackTextures(textures, 1, 4096);

			for (int i = 0; i < sprites.Count; ++i)
			{
				sprites[i].Rect = NGUIMath.ConvertToPixels(rects[i], tex.width, tex.height, true);
			}
		}
		static UIAtlas.Sprite AddSprite(ICollection<UIAtlas.Sprite> sprites, SpriteEntry se)
		{
			UIAtlas.Sprite sprite = sprites.FirstOrDefault(sp => sp.name == se.Tex.name);

			// See if this sprite already exists

			if (sprite != null)
			{
				float x0 = sprite.inner.xMin - sprite.outer.xMin;
				float y0 = sprite.inner.yMin - sprite.outer.yMin;
				float x1 = sprite.outer.xMax - sprite.inner.xMax;
				float y1 = sprite.outer.yMax - sprite.inner.yMax;

				sprite.outer = se.Rect;
				sprite.inner = se.Rect;

				sprite.inner.xMin = Mathf.Max(sprite.inner.xMin + x0, sprite.outer.xMin);
				sprite.inner.yMin = Mathf.Max(sprite.inner.yMin + y0, sprite.outer.yMin);
				sprite.inner.xMax = Mathf.Min(sprite.inner.xMax - x1, sprite.outer.xMax);
				sprite.inner.yMax = Mathf.Min(sprite.inner.yMax - y1, sprite.outer.yMax);
			}
			else
			{
				sprite = new UIAtlas.Sprite {name = se.Tex.name, outer = se.Rect, inner = se.Rect};
				sprites.Add(sprite);
			}

			float width = Mathf.Max(1f, sprite.outer.width);
			float height = Mathf.Max(1f, sprite.outer.height);

			// Sprite's padding values are relative to width and height
			sprite.paddingLeft = se.MinX / width;
			sprite.paddingRight = se.MaxX / width;
			sprite.paddingTop = se.MaxY / height;
			sprite.paddingBottom = se.MinY / height;
			return sprite;
		}

		#endregion


	}
}