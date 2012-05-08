// NGUI import/export plugin
// SpriterData_NGUI.cs
// Spriter Data API - Unity
//  
// Authors:
//       Josh Montoute <josh@thinksquirrel.com>
//
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
// The SCML file and all images must be imported and within the project's Assets folder.
// This limitation may be removed in a future version.
//
// The default implementation of ImportData works in two phases:
//
// Phase 1 creates a sprite atlas, with all textures.
// Phase 2 creates a character prefab and animation data.
//
// Overrides are planned to allow support for updating an existing atlas and character prefab.
// 
// Due to some strange behaviour in the Unity Editor, animations are not very editable once imported.

//
// ---------------------------------------------
// Pre-processor directives:
// ---------------------------------------------
//

// NGUI_FREE: Allows compatibility with NGUI Free Edition.
// Enable this pre-processor define if NGUI gives compile-time errors.
// #define NGUI_FREE

// NO_CURVES: Disables animation curve interpolation between Spriter frames.
// Keep this define enabled until Spriter properly supports animation curves.
#define NO_CURVES

//
// ---------------------------------------------
//

using BrashMonkey.Spriter.Data;
using BrashMonkey.Spriter.Data.IO;
using BrashMonkey.Spriter.Data.ObjectModel;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace BrashMonkey.Spriter.DataPlugins.NGUI
{
	public class SpriterData_NGUI : ISpriterData
	{
		#region Menu items
		[MenuItem("Spriter/NGUI Plugin/Create new character")]
		static void CreateAtlas()
		{
			var data = new SpriterData_NGUI();
			
			string path = EditorUtility.OpenFilePanel("Open Spriter File", Application.dataPath, "scml");
			
			if (!string.IsNullOrEmpty(path))
			{
				data.LoadSCML(path);
				data.ImportData();
			}
		}
		#endregion
		
		#region Internal types
		class SpriteEntry
		{
			public Texture2D tex;	// Sprite texture -- original texture or a temporary texture
			public Rect rect;		// Sprite's outer rectangle within the generated texture atlas
			public int minX = 0;	// Padding, if any (set if the sprite is trimmed)
			public int maxX = 0;
			public int minY = 0;
			public int maxY = 0;
			public bool temporaryTexture = false;	// Whether the texture is temporary and should be deleted
		}
		[System.Flags]
		enum TangentMode
		{
			Editable = 0,
			Smooth = 1,
			Linear = 2,
			Stepped = 3,
		}
		#endregion
		
		#region Private fields
		private ISpriterCharacter mCharacter;
		private List<ISpriterFrame> mFrames;
		private string mPath;
		private Material mat;
		private GameObject go;
		const int kLeftTangentMask = 1 << 1 | 1 << 2;
		const int kRightTangentMask = 1 << 3 | 1 << 4;	
		#endregion
		
		#region Sprite atlas creation
		private void CreateSpriteAtlas()
		{
			// Create paths and names based on selection
			UISettings.atlasName = mCharacter.name;
			string prefabPath = NGUIEditorTools.GetSelectionFolder() + UISettings.atlasName + " Atlas.prefab";
			string matPath = NGUIEditorTools.GetSelectionFolder() + UISettings.atlasName + " Atlas.mat";
			
			// Create the material
			Shader shader = Shader.Find("Unlit/Transparent Colored");
			mat = new Material(shader);

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
			go = new GameObject(UISettings.atlasName);
#if NGUI_FREE
			go.AddComponent<UIAtlas>().material = mat;
#else
			go.AddComponent<UIAtlas>().spriteMaterial = mat;
#endif
			// Update the prefab
#if UNITY_3_4
			EditorUtility.ReplacePrefab(go, prefab);
#else
			PrefabUtility.ReplacePrefab(go, prefab);
#endif
			UnityEngine.Object.DestroyImmediate(go);
			AssetDatabase.Refresh();

			// Select the atlas
			go = AssetDatabase.LoadAssetAtPath(prefabPath, typeof(GameObject)) as GameObject;
			UISettings.atlas = go.GetComponent<UIAtlas>();		
			
			// Textures
			List<Texture> texLoad = new List<Texture>();
			List<string> paths = new List<string>();
			foreach(var frame in mFrames)
			{
				foreach(var sprite in frame.sprites)
				{
					string p = mPath + sprite.imagePath;
					if (!paths.Contains(p))
					{
						var tex = Resources.LoadAssetAtPath(p, typeof(Texture)) as Texture;
						texLoad.Add(tex);
						paths.Add(p);
					}
				}
			}
			
			// Update the atlas
			UpdateAtlas(texLoad);
		}	
		static void UpdateAtlas(List<Texture> textures)
		{
			// Create a list of sprites using the collected textures
			List<SpriteEntry> sprites = CreateSprites(textures);
	
			UpdateAtlas(UISettings.atlas, sprites);
		}
		static List<SpriteEntry> CreateSprites (List<Texture> textures)
		{
			List<SpriteEntry> list = new List<SpriteEntry>();
	
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
	
				int newWidth  = (xmax - xmin) + 1;
				int newHeight = (ymax - ymin) + 1;
	
				if (newWidth > 0 && newHeight > 0)
				{
					SpriteEntry sprite = new SpriteEntry();
					sprite.rect = new Rect(0f, 0f, oldTex.width, oldTex.height);
	
					if (newWidth == oldWidth && newHeight == oldHeight)
					{
						sprite.tex = oldTex;
						sprite.temporaryTexture = false;
					}
					else
					{
						Color32[] newPixels = new Color32[newWidth * newHeight];
	
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
						sprite.temporaryTexture = true;
						sprite.tex = new Texture2D(newWidth, newHeight);
						sprite.tex.name = oldTex.name;
						sprite.tex.SetPixels32(newPixels);
						sprite.tex.Apply();
	
						// Remember the padding offset
						sprite.minX = xmin;
						sprite.maxX = oldWidth - newWidth - xmin;
						sprite.minY = ymin;
						sprite.maxY = oldHeight - newHeight - ymin;
					}
					list.Add(sprite);
				}
			}
			return list;
		}
		static void UpdateAtlas (UIAtlas atlas, List<SpriteEntry> sprites)
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
#if NGUI_FREE
				atlas.sprites.Clear();
#else
				atlas.spriteList.Clear();
#endif
				string path = NGUIEditorTools.GetSaveableTexturePath(atlas);
#if NGUI_FREE
				atlas.material.mainTexture = null;
#else
				atlas.spriteMaterial.mainTexture = null;
#endif
				if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
			}
			EditorUtility.SetDirty(atlas.gameObject);
		}
		static Texture2D UpdateTexture (UIAtlas atlas, List<SpriteEntry> sprites)
		{
			// Get the texture for the atlas
			Texture2D tex = atlas.texture as Texture2D;
			string oldPath = (tex != null) ? AssetDatabase.GetAssetPath(tex.GetInstanceID()) : "";
			string newPath = NGUIEditorTools.GetSaveableTexturePath(atlas);
	
			if (tex == null || oldPath != newPath)
			{
				// Create a new texture for the atlas
				tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
	
				// Pack the sprites into this texture
				PackTextures(tex, sprites);
				byte[] bytes = tex.EncodeToPNG();
				System.IO.File.WriteAllBytes(newPath, bytes);
				bytes = null;
	
				// Load the texture we just saved as a Texture2D
				AssetDatabase.Refresh();
				tex = NGUIEditorTools.ImportTexture(newPath, false, true);
	
				// Update the atlas texture
				if (tex == null) Debug.LogError("Failed to load the created atlas saved as " + newPath);
#if NGUI_FREE				
				else atlas.material.mainTexture = tex;
#else
				else atlas.spriteMaterial.mainTexture = tex;
#endif
			}
			else
			{
				// Make the atlas readable so we can save it
				tex = NGUIEditorTools.ImportTexture(oldPath, true, false);
	
				// Pack all sprites into atlas texture
				PackTextures(tex, sprites);
				byte[] bytes = tex.EncodeToPNG();
				System.IO.File.WriteAllBytes(newPath, bytes);
				bytes = null;
	
				// Re-import the newly created texture, turning off the 'readable' flag
				AssetDatabase.Refresh();
				tex = NGUIEditorTools.ImportTexture(newPath, false, false);
			}
			return tex;
		}
		static void ReplaceSprites (UIAtlas atlas, List<SpriteEntry> sprites)
		{
			// Get the list of sprites we'll be updating
#if NGUI_FREE
			List<UIAtlas.Sprite> spriteList = atlas.sprites;
#else
			List<UIAtlas.Sprite> spriteList = atlas.spriteList;
#endif
			List<UIAtlas.Sprite> kept = new List<UIAtlas.Sprite>();
	
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
		static void ReleaseSprites (List<SpriteEntry> sprites)
		{
			foreach (SpriteEntry se in sprites)
			{
				if (se.temporaryTexture)
				{
					NGUITools.Destroy(se.tex);
					se.tex = null;
				}
			}
			Resources.UnloadUnusedAssets();
		}
		static List<Texture2D> LoadTextures(List<Texture> textures)
		{
			List<Texture2D> list = new List<Texture2D>();
	
			foreach (Texture tex in textures)
			{
				Texture2D t2 = NGUIEditorTools.ImportTexture(tex, true, false);
				if (t2 != null) list.Add(t2);
			}
			return list;
		}
		static void PackTextures (Texture2D tex, List<SpriteEntry> sprites)
		{
			Texture2D[] textures = new Texture2D[sprites.Count];
			for (int i = 0; i < sprites.Count; ++i) textures[i] = sprites[i].tex;
	
			Rect[] rects = tex.PackTextures(textures, 1, 4096);
	
			for (int i = 0; i < sprites.Count; ++i)
			{
				sprites[i].rect = NGUIMath.ConvertToPixels(rects[i], tex.width, tex.height, true);
			}
		}
		static UIAtlas.Sprite AddSprite (List<UIAtlas.Sprite> sprites, SpriteEntry se)
		{
			UIAtlas.Sprite sprite = null;
	
			// See if this sprite already exists
			foreach (UIAtlas.Sprite sp in sprites)
			{
				if (sp.name == se.tex.name)
				{
					sprite = sp;
					break;
				}
			}
	
			if (sprite != null)
			{
				float x0 = sprite.inner.xMin - sprite.outer.xMin;
				float y0 = sprite.inner.yMin - sprite.outer.yMin;
				float x1 = sprite.outer.xMax - sprite.inner.xMax;
				float y1 = sprite.outer.yMax - sprite.inner.yMax;
	
				sprite.outer = se.rect;
				sprite.inner = se.rect;
	
				sprite.inner.xMin = Mathf.Max(sprite.inner.xMin + x0, sprite.outer.xMin);
				sprite.inner.yMin = Mathf.Max(sprite.inner.yMin + y0, sprite.outer.yMin);
				sprite.inner.xMax = Mathf.Min(sprite.inner.xMax - x1, sprite.outer.xMax);
				sprite.inner.yMax = Mathf.Min(sprite.inner.yMax - y1, sprite.outer.yMax);
			}
			else
			{
				sprite = new UIAtlas.Sprite();
				sprite.name = se.tex.name;
				sprite.outer = se.rect;
				sprite.inner = se.rect;
				sprites.Add(sprite);
			}
	
			float width  = Mathf.Max(1f, sprite.outer.width);
			float height = Mathf.Max(1f, sprite.outer.height);
	
			// Sprite's padding values are relative to width and height
			sprite.paddingLeft	 = se.minX / width;
			sprite.paddingRight  = se.maxX / width;
			sprite.paddingTop	 = se.maxY / height;
			sprite.paddingBottom = se.minY / height;
			return sprite;
		}
		#endregion
		
		#region Character prefab creation
		private void CreateCharacterPrefab()
		{
			// All character creation is done in the hierarchy, then saved to a prefab
			
			// Create root object
			Transform characterRoot = new GameObject(mCharacter.name).transform;
			characterRoot.gameObject.AddComponent<Animation>();
			var col = characterRoot.gameObject.AddComponent<BoxCollider>();
			col.center = new Vector3(mCharacter.boundingBox.center.x, -mCharacter.boundingBox.center.y, 0);
			col.extents = new Vector3(mCharacter.boundingBox.width / 2f, mCharacter.boundingBox.height / 2f, 1f);
			col.isTrigger = true;
			
			if (Selection.activeTransform)
			{
				characterRoot.parent = Selection.activeTransform;
				characterRoot.localScale = Vector3.one;
			}
			
			// Create all initial sprite objects beforehand
			foreach(ISpriterAnimation a in mCharacter.animations)
			{
				foreach(ISpriterKeyframe kf in a.keyframes)
				{
					ISpriterFrame f = FindFrame(kf.name);
					
					foreach(ISpriterSprite s in f.sprites)
					{
						var t = FindSpriteObject(characterRoot, s).transform;
						t.localPosition = Vector3.zero;
						t.localRotation = Quaternion.identity;
					}
				}
				
			}
			
			// Tracks the very last processed keyframe for display purposes
			ISpriterFrame endFrame = mFrames.Count > 0 ? mFrames[mFrames.Count - 1] : null;
			
			// Go through each animation
			int ind = 0;
			foreach(ISpriterAnimation anim in mCharacter.animations)
			{
				// Create an animation clip
				AnimationClip clip = new AnimationClip();
				clip.EnsureQuaternionContinuity();
				clip.name = anim.name;
				clip.wrapMode = WrapMode.Loop;
				
				// Create animation curves
				Dictionary<string, AnimationCurve>[] curves = new Dictionary<string, AnimationCurve>[14];
				
				for(int i = 0; i < 14; i++)
				{
					curves[i] = new Dictionary<string, AnimationCurve>();
				}
				
				// The current time in the animation
				float currentTime = 0;
				ISpriterFrame lastFrame = null;
				ISpriterKeyframe lastKeyframe = null;
				
				int l = anim.keyframes.Count;
				
				// Go through each keyframe
				for(int i = 0; i < l; i++)
				{
					// Record the frame
					currentTime = RecordFrame(characterRoot, anim.keyframes[i], ref lastFrame, ref lastKeyframe, endFrame, currentTime, clip.frameRate, curves);
				}
				
				// Bind the animation
				BindAnimation(clip, curves);
				
				// Save the animation
				string p = NGUIEditorTools.GetSelectionFolder() + characterRoot.name + "@" + clip.name + ".anim";
				if (AssetDatabase.LoadAssetAtPath(p, typeof(AnimationClip)))
				{
					AssetDatabase.DeleteAsset(p);
					AssetDatabase.CreateAsset(clip, p);
				}
				else
				{
					AssetDatabase.CreateAsset(clip, p);
				}
				
				// Update the reference
				clip = AssetDatabase.LoadAssetAtPath(p, typeof(AnimationClip)) as AnimationClip;
				
				// Add the clip to the root object
				characterRoot.animation.AddClip(clip, clip.name);
				
				if (!characterRoot.animation.clip)
				{
					characterRoot.animation.clip = characterRoot.animation.GetClip(clip.name);
				}
				ind++;
			}
							
			SaveAssets(characterRoot.gameObject);
		}	
		float RecordFrame(Transform characterRoot, ISpriterKeyframe keyframe, ref ISpriterFrame lastFrame, ref ISpriterKeyframe lastKeyframe, ISpriterFrame endFrame, float currentTime, float frameRate, Dictionary<string, AnimationCurve>[] curves)
		{
			ISpriterFrame frame = FindFrame(keyframe.name);
					
			if (frame == null)
			{
				Debug.LogError(string.Format("Frame not found: {1}", keyframe.name)); 
				return currentTime;
			}
			
			// Go through each sprite, keeping track of what is processed
			List<string> processedSprites = new List<string>();
			
			int spriteDepth = 0;
			
			foreach(ISpriterSprite sprite in frame.sprites)
			{
				// Gets the last angle
				float lastAngle = 0;
				
				if (lastFrame != null)
				{
					foreach(var s in lastFrame.sprites)
					{
						if (s.imagePath.Equals(sprite.imagePath))
						{
							lastAngle = s.angle;
							break;
						}
					}
				}
				
				// Find the sprite object
				UISprite uiSprite = FindSpriteObject(characterRoot, sprite);
				
				uiSprite.transform.localPosition = new Vector3(sprite.spriteRect.x, -sprite.spriteRect.y, 0);
				
				float xFlip = sprite.xFlip ? -1 : 1;
				float yFlip = sprite.yFlip ? -1 : 1;
				
				UIAtlas.Sprite atlasSprite = uiSprite.atlas.GetSprite(uiSprite.spriteName);
				float pW = (atlasSprite.paddingLeft + atlasSprite.paddingRight) * (atlasSprite.outer.width);
				float pH = (atlasSprite.paddingTop + atlasSprite.paddingBottom) * (atlasSprite.outer.height);
				float w = sprite.spriteRect.width - pW;
				float h = sprite.spriteRect.height - pH;
				uiSprite.transform.localScale = new Vector3(w * xFlip, h * yFlip, 1);
				
				Quaternion oldRotation = uiSprite.transform.localRotation;
				Quaternion newRotation = Quaternion.Slerp(oldRotation, oldRotation * Quaternion.AngleAxis(sprite.angle - lastAngle, Vector3.forward), 1f);
				uiSprite.transform.localRotation = newRotation;
					
				uiSprite.color = sprite.color;
				
				uiSprite.depth = spriteDepth++;
				
				AnimationCurve currentCurve_xPos = null;
				AnimationCurve currentCurve_yPos = null;
				AnimationCurve currentCurve_zPos = null;
				AnimationCurve currentCurve_xScale = null;
				AnimationCurve currentCurve_yScale = null;
				AnimationCurve currentCurve_zScale = null;
				AnimationCurve currentCurve_xRot = null;
				AnimationCurve currentCurve_yRot = null;
				AnimationCurve currentCurve_zRot = null;
				AnimationCurve currentCurve_wRot = null;
				AnimationCurve currentCurve_toggle = null;
				AnimationCurve currentCurve_colorR = null;
				AnimationCurve currentCurve_colorG = null;
				AnimationCurve currentCurve_colorB = null;
				
				if (!curves[0].ContainsKey(uiSprite.spriteName))
				{
					// New object, create new animation curves
					currentCurve_xPos = new AnimationCurve();
					currentCurve_yPos = new AnimationCurve();
					currentCurve_zPos = new AnimationCurve();
					currentCurve_xScale = new AnimationCurve();
					currentCurve_yScale = new AnimationCurve();
					currentCurve_zScale = new AnimationCurve();
					currentCurve_xRot = new AnimationCurve();
					currentCurve_yRot = new AnimationCurve();
					currentCurve_zRot = new AnimationCurve();
					currentCurve_wRot = new AnimationCurve();
					currentCurve_colorR = new AnimationCurve();
					currentCurve_colorG = new AnimationCurve();
					currentCurve_colorB = new AnimationCurve();
					
					curves[0].Add(uiSprite.spriteName, currentCurve_xPos);
					curves[1].Add(uiSprite.spriteName, currentCurve_yPos);
					curves[2].Add(uiSprite.spriteName, currentCurve_zPos);
					curves[3].Add(uiSprite.spriteName, currentCurve_xScale);
					curves[4].Add(uiSprite.spriteName, currentCurve_yScale);
					curves[5].Add(uiSprite.spriteName, currentCurve_zScale);
					curves[6].Add(uiSprite.spriteName, currentCurve_xRot);
					curves[7].Add(uiSprite.spriteName, currentCurve_yRot);
					curves[8].Add(uiSprite.spriteName, currentCurve_zRot);
					curves[9].Add(uiSprite.spriteName, currentCurve_wRot);
					curves[11].Add(uiSprite.spriteName, currentCurve_colorR);
					curves[12].Add(uiSprite.spriteName, currentCurve_colorG);
					curves[13].Add(uiSprite.spriteName, currentCurve_colorB);
					
				}
				else
				{
					// Get the current animation curves
					currentCurve_xPos = curves[0][uiSprite.spriteName];
					currentCurve_yPos = curves[1][uiSprite.spriteName];
					currentCurve_zPos = curves[2][uiSprite.spriteName];
					currentCurve_xScale = curves[3][uiSprite.spriteName];
					currentCurve_yScale = curves[4][uiSprite.spriteName];
					currentCurve_zScale = curves[5][uiSprite.spriteName];
					currentCurve_xRot = curves[6][uiSprite.spriteName];
					currentCurve_yRot = curves[7][uiSprite.spriteName];
					currentCurve_zRot = curves[8][uiSprite.spriteName];
					currentCurve_wRot = curves[9][uiSprite.spriteName];
					currentCurve_colorR = curves[11][uiSprite.spriteName];
					currentCurve_colorG = curves[12][uiSprite.spriteName];
					currentCurve_colorB = curves[13][uiSprite.spriteName];
				}
				
				if (!curves[10].ContainsKey(uiSprite.spriteName))
				{
					currentCurve_toggle = new AnimationCurve();
					curves[10].Add(uiSprite.spriteName, currentCurve_toggle);
				}
				else
				{
					currentCurve_toggle = curves[10][uiSprite.spriteName];
				}
				
				int keyPos = currentCurve_toggle.AddKey(SteppedKeyframe(currentTime, uiSprite.color.a));
				
				if (currentTime > 0)
				{	
					currentCurve_xPos.AddKey(LinearKeyframe(0, uiSprite.transform.localPosition.x));
					currentCurve_yPos.AddKey(LinearKeyframe(0, uiSprite.transform.localPosition.y));
					currentCurve_zPos.AddKey(LinearKeyframe(0, uiSprite.transform.localPosition.z));
					
					currentCurve_xScale.AddKey(LinearKeyframe(0, uiSprite.transform.localScale.x));
					currentCurve_yScale.AddKey(LinearKeyframe(0, uiSprite.transform.localScale.y));
					currentCurve_zScale.AddKey(LinearKeyframe(0, uiSprite.transform.localScale.z));
					
					Quaternion slerp = Quaternion.Slerp(oldRotation, uiSprite.transform.localRotation, 1f);
					
					currentCurve_xRot.AddKey(LinearKeyframe(0, slerp.x));
					currentCurve_yRot.AddKey(LinearKeyframe(0, slerp.y));
					currentCurve_zRot.AddKey(LinearKeyframe(0, slerp.z));
					currentCurve_wRot.AddKey(LinearKeyframe(0, slerp.w));
					
					currentCurve_colorR.AddKey(LinearKeyframe(0, uiSprite.color.r));
					currentCurve_colorG.AddKey(LinearKeyframe(0, uiSprite.color.g));
					currentCurve_colorB.AddKey(LinearKeyframe(0, uiSprite.color.b));
					
					if (keyPos > 0)
					{
						// Sprite was off
						if (currentCurve_toggle.keys[keyPos - 1].value < uiSprite.color.a)
						{
							currentCurve_toggle.AddKey(SteppedKeyframe(currentTime - (.001f / frameRate), 0));
						}
					}
					
					currentCurve_toggle.AddKey(SteppedKeyframe(0, 0));
				}

#if NO_CURVES
				float f = ((keyframe.duration / 100f)) - (.001f / frameRate);
				
				currentCurve_xPos.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localPosition.x));
				currentCurve_yPos.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localPosition.y));
				currentCurve_zPos.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localPosition.z));
				
				currentCurve_xScale.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localScale.x));
				currentCurve_yScale.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localScale.y));
				currentCurve_zScale.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localScale.z));
				
				currentCurve_xRot.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localRotation.x));
				currentCurve_yRot.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localRotation.y));
				currentCurve_zRot.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localRotation.z));
				currentCurve_wRot.AddKey(SteppedKeyframe(currentTime, uiSprite.transform.localRotation.w));
				
				currentCurve_colorR.AddKey(SteppedKeyframe(currentTime, uiSprite.color.r));
				currentCurve_colorG.AddKey(SteppedKeyframe(currentTime, uiSprite.color.g));
				currentCurve_colorB.AddKey(SteppedKeyframe(currentTime, uiSprite.color.b));
				
				currentCurve_xPos.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localPosition.x));
				currentCurve_yPos.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localPosition.y));
				currentCurve_zPos.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localPosition.z));
				
				currentCurve_xScale.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localScale.x));
				currentCurve_yScale.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localScale.y));
				currentCurve_zScale.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localScale.z));
				
				currentCurve_xRot.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localRotation.x));
				currentCurve_yRot.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localRotation.y));
				currentCurve_zRot.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localRotation.z));
				currentCurve_wRot.AddKey(SteppedKeyframe(currentTime + f, uiSprite.transform.localRotation.w));
				
				currentCurve_colorR.AddKey(SteppedKeyframe(currentTime + f, uiSprite.color.r));
				currentCurve_colorG.AddKey(SteppedKeyframe(currentTime + f, uiSprite.color.g));
				currentCurve_colorB.AddKey(SteppedKeyframe(currentTime + f, uiSprite.color.b));
#endif
				
				if (frame == endFrame)
					uiSprite.color = sprite.color;
				else
					uiSprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 0);
				
				// Mark as processed
				processedSprites.Add(uiSprite.spriteName);
				
			}	
			
			// Check through all existing sprite objects
			foreach(Transform transform in characterRoot)
			{
				// Is this needed?
				if (transform == characterRoot)
					continue;
				
				if (!processedSprites.Contains(transform.name))
				{	
					float t = currentTime;
					
					AnimationCurve val;
					if (curves[10].TryGetValue(transform.name, out val))
					{
						int pos = val.AddKey(SteppedKeyframe(t, 0));
						
						if (pos > 0)
						{
							if (val.keys[pos - 1].value > 0)
							{
								val.AddKey(SteppedKeyframe(t - (.001f / frameRate), val.keys[pos -1].value));
							}
						}
					}
					else
					{
						val = new AnimationCurve();
						curves[10].Add(transform.name, val);
					
						int pos = val.AddKey(SteppedKeyframe(t, 0));
						
						if (pos > 0)
						{
							if (val.keys[pos - 1].value > 0)
							{
								val.AddKey(SteppedKeyframe(t - (.001f / frameRate), val.keys[pos -1].value));
							}
						}
					}
				}
			}
			
			// Advance the time
			currentTime += (keyframe.duration / 100f);
			lastKeyframe = keyframe;
			lastFrame = frame;
			
			return currentTime;
		}
		float LerpUnclamped (float from, float to, float value)
		{
			return (1.0f - value)*from + value*to;
    	}				
		void SetKeyTangentMode (ref Keyframe key, int leftRight, TangentMode mode)
		{
			if (leftRight == 0)
			{
				key.tangentMode &= ~kLeftTangentMask;
				key.tangentMode |= (int)mode << 1;
			}
			else
			{
				key.tangentMode &= ~kRightTangentMask;
				key.tangentMode |= (int)mode << 3;
			}
			
			if (GetKeyTangentMode (key, leftRight) != mode)
				Debug.Log("bug");
		}
		TangentMode GetKeyTangentMode (Keyframe key, int leftRight)
		{
			if (leftRight == 0)
			{
				return (TangentMode)((key.tangentMode & kLeftTangentMask) >> 1);
			}
			else
			{
				return (TangentMode)((key.tangentMode & kRightTangentMask) >> 3);
			}
		}
		// TODO: Once curves are supported, use linear keyframes where possible (or even better, get tangents straight from SCML)
		Keyframe LinearKeyframe(float time, float value)
		{
#if NO_CURVES
			return SteppedKeyframe(time, value);
#else
			Keyframe result = new Keyframe();
			result.value = value;
			result.time = time;
			SetKeyTangentMode(ref result, 0, TangentMode.Editable | TangentMode.Linear);
			SetKeyTangentMode(ref result, 1, TangentMode.Editable | TangentMode.Linear);
			
			return result;
#endif
		}
		Keyframe SteppedKeyframe(float time, float value)
		{
			Keyframe result = new Keyframe();
			result.value = value;
			result.time = time;
			SetKeyTangentMode(ref result, 0, TangentMode.Editable | TangentMode.Stepped);
			SetKeyTangentMode(ref result, 1, TangentMode.Editable | TangentMode.Stepped);
			
			return result;
		}
		void BindAnimation(AnimationClip clip, Dictionary<string, AnimationCurve>[] curves)
		{
			// Bind the curves to the animation clip
			foreach(var kvp in curves[0])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localPosition.x", kvp.Value);
				}
			}
			foreach(var kvp in curves[1])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localPosition.y", kvp.Value);
				}
			}
			foreach(var kvp in curves[2])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localPosition.z", kvp.Value);
				}
			}
			
			foreach(var kvp in curves[3])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localScale.x", kvp.Value);
				}
			}
			foreach(var kvp in curves[4])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localScale.y", kvp.Value);
				}
			}
			foreach(var kvp in curves[5])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localScale.z", kvp.Value);
				}
			}
			
			
			foreach(var kvp in curves[6])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localRotation.x", kvp.Value);
				}
			}
			foreach(var kvp in curves[7])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localRotation.y", kvp.Value);
				}
			}
			foreach(var kvp in curves[8])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localRotation.z", kvp.Value);
				}
			}
			foreach(var kvp in curves[9])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(Transform), "localRotation.w", kvp.Value);
				}
			}
			foreach(var kvp in curves[10])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(SpriterNGUIColorHelper), "color.a", kvp.Value);
				}
			}
			foreach(var kvp in curves[11])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(SpriterNGUIColorHelper), "color.r", kvp.Value);
				}
			}
			foreach(var kvp in curves[12])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(SpriterNGUIColorHelper), "color.g", kvp.Value);
				}
			}
			foreach(var kvp in curves[13])
			{
				if (kvp.Value.length > 1)
				{
					clip.SetCurve(kvp.Key, typeof(SpriterNGUIColorHelper), "color.b", kvp.Value);
				}
			}
		}
		void SaveAssets(GameObject root)
		{	
#if UNITY_3_5
			Transform parent = root.transform.parent;
			var prefab = PrefabUtility.CreatePrefab(NGUIEditorTools.GetSelectionFolder() + root.name + ".prefab", root);
			UnityEngine.Object.DestroyImmediate(root);
			var newObj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
			newObj.transform.parent = parent;
			newObj.transform.localScale = Vector3.one;
#else
			Transform parent = root.transform.parent;
			var prefab = EditorUtility.CreateEmptyPrefab(NGUIEditorTools.GetSelectionFolder() + root.name + ".prefab");
			prefab = EditorUtility.ReplacePrefab(root, prefab);
			UnityEngine.Object.DestroyImmediate(root);
			var newObj = EditorUtility.InstantiatePrefab(prefab) as GameObject;
			newObj.transform.parent = parent;
			newObj.transform.localScale = Vector3.one;
#endif		
			AssetDatabase.Refresh();
		}
		UISprite FindSpriteObject(Transform characterRoot, ISpriterSprite sprite)
		{
			// Find existing
			foreach(Transform transform in characterRoot)
			{
				// Is this needed?
				if (transform == characterRoot)
					continue;
				
				if (transform.name == GetSpriteName(sprite.imagePath))
				{
					return transform.GetComponent<UISprite>();
				}
			}
			
			// None exists
			GameObject go = new GameObject(GetSpriteName(sprite.imagePath));
			go.transform.parent = characterRoot;
			go.transform.localPosition = Vector3.zero;
			UISprite result = go.AddComponent<UISprite>();
			go.AddComponent<SpriterNGUIColorHelper>();
			result.atlas = UISettings.atlas;
			result.spriteName = GetSpriteName(sprite.imagePath);
			result.pivot = UIWidget.Pivot.TopLeft;
			return result;
			
		}
		ISpriterFrame FindFrame(string name)
		{
			foreach(var frame in mFrames)
			{
				if (frame.name.Equals(name))
					return frame;
			}
			
			return null;
		}	
		string GetSpriteName(string imagePath)
		{
			int index = imagePath.LastIndexOf('\\') + 1;
			string file = index == -1 ? imagePath : imagePath.Substring(index);
			return file.Substring(0, file.LastIndexOf('.'));
		}
		#endregion
		
		#region ISpriterData implementation
		public void LoadSCML(string path)
		{
			LoadSCML<SpriterData_NGUI, SpriterCharacter, SpriterAnimation, SpriterFrame, SpriterKeyframe, SpriterSprite>(path);
			int len = Application.dataPath.Length + 1;
			string sub = path.Substring(len);
			int index = sub.LastIndexOf('/') + 1;
			mPath = "Assets/" + sub.Substring(0, index);
		}

		public void SaveSCML(string path)
		{
			throw new System.NotImplementedException();
		}
		
		public void ExportData()
		{
			throw new System.NotImplementedException();
		}
		
		public void ImportData()
		{
			if (string.IsNullOrEmpty(mPath))
				return;
						
			CreateSpriteAtlas();
			CreateCharacterPrefab();
		}
		
		public ISpriterCharacter character
		{
			get
			{
				return mCharacter;
			}
			set
			{
				mCharacter = value;
			}
		}

		public List<ISpriterFrame> frames
		{
			get
			{
				return mFrames;
			}
			set
			{
				mFrames = value;
			}
		}
		#endregion
	
	}
}