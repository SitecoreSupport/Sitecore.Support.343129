// Sitecore.XA.Feature.Composites.EventHandlers.SaveCompositeItems
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.DependencyInjection;
using Sitecore.Diagnostics;
using Sitecore.Events;
using Sitecore.ExperienceEditor;
using Sitecore.Mvc.Extensions;
using Sitecore.Pipelines;
using Sitecore.Pipelines.ResolveRenderingDatasource;
using Sitecore.Web;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.XA.Feature.Composites;
using Sitecore.XA.Feature.Composites.Extensions;
using Sitecore.XA.Feature.Composites.Models;
using Sitecore.XA.Feature.Composites.Services;
using Sitecore.XA.Foundation.Abstractions;
using Sitecore.XA.Foundation.Presentation.Layout;
using Sitecore.XA.Foundation.SitecoreExtensions;
using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Sitecore.Support.XA.Feature.Composites.EventHandlers
{

    public class SaveCompositeItems
    {
        protected IContext Context
        {
            get;
        } = ServiceLocator.ServiceProvider.GetService<IContext>();


        public void OnItemSaving(object sender, EventArgs args)
        {
            if (ServiceLocator.ServiceProvider.GetService<ICompositesConfiguration>().OnPageEditingEnabled && !JobsHelper.IsPublishing())
            {
                Assert.ArgumentNotNull(sender, "sender");
                Assert.ArgumentNotNull(args, "args");
                Item item = Event.ExtractParameter(args, 0) as Item;
                PropagateLayoutChanges(item);
            }
        }

        protected virtual void PropagateLayoutChanges(Item item)
        {
            Assert.ArgumentNotNull(item, "item");
            if (item.InheritsFrom(Sitecore.XA.Feature.Composites.Templates.CompositeSection.ID))
            {
                return;
            }
            Item item2 = item.Database.GetItem(item.ID, item.Language, item.Version);
            LayoutField layoutField = new LayoutField(item);
            string value = layoutField.Value;
            string value2 = new LayoutField(item2).Value;
            if (Registry.GetString(Sitecore.ExperienceEditor.Constants.RegistryKeys.EditAllVersions) == "on")
            {
                value = item.Fields[FieldIDs.LayoutField].Value;
                value2 = item2.Fields[FieldIDs.LayoutField].Value;
            }
            if (Regex.IsMatch(XmlDeltas.GetDelta(value, value2), "section-[title|content]"))
            {
                value = GetNewLayoutFromRequest(value);
                LayoutModel layoutModel = new LayoutModel(value);
                foreach (DeviceModel item4 in layoutModel.Devices.DevicesCollection)
                {
                    List<RenderingModel> injectedCompositeRenderings = GetInjectedCompositeRenderings(item4).ToList();
                    ICompositeService compositeService = ServiceLocator.ServiceProvider.GetService<ICompositeService>();
                    foreach (RenderingModel item5 in from model in item4.Renderings.RenderingsCollection
                                                     where compositeService.IsCompositeRendering(model.Id)
                                                     select model into c
                                                     orderby c.Placeholder.Length descending
                                                     select c)
                    {
                        Item compositeDatasource = GetCompositeDatasource(item, item5.DataSource);
                        if (compositeDatasource != null)
                        {
                            Item[] array = compositeService.GetCompositeItems(compositeDatasource).ToArray();
                            for (int i = 0; i < array.Length; i++)
                            {
                                Item item3 = array[i];
                                IList<RenderingModel> renderingsFromCompositeSection = GetRenderingsFromCompositeSection(item5, i, injectedCompositeRenderings);
                                #region SUPPORT PATCH 343129
                                /* Just one if statement added for this patch,
                                 * If we do not get any renderings, no need to update based on them.*/
                                if (renderingsFromCompositeSection.Count > 0)
                                {
                                    #endregion
                                    TransformPlaceholderPaths(item5, i, renderingsFromCompositeSection);
                                    UpdateDatasourceTokens(item3, renderingsFromCompositeSection, item);
                                    LayoutModel layoutModel2 = new LayoutModel(item3);
                                    LayoutModel layoutModel3 = new LayoutModel(item3);
                                    layoutModel3.RemoveAllRenderings(item4.DeviceId);
                                    layoutModel3.AddRenderings(item4.DeviceId, renderingsFromCompositeSection);
                                    string text = layoutModel3.ToString();
                                    if (!XmlDeltas.GetDelta(text, layoutModel2.ToString()).IsEmptyOrNull())
                                    {
                                        SetLayoutValue(layoutModel2, text);
                                    }
                                }

                            }
                        }
                    }
                    RemoveInjectedCompositeRenderings(item4);
                }
                if (Registry.GetString(Sitecore.ExperienceEditor.Constants.RegistryKeys.EditAllVersions) == "on")
                {
                    item.Fields[FieldIDs.LayoutField].Value = layoutModel.ToString();
                }
                else
                {
                    layoutField.Value = layoutModel.ToString();
                }
            }
        }

        protected virtual void UpdateDatasourceTokens(Item compositeDatasourceChild, IEnumerable<RenderingModel> renderings, Item currentItem)
        {
            string text = "local:";
            string text2 = "page:";
            foreach (RenderingModel item3 in from r in renderings
                                             where r.DataSource != null
                                             select r)
            {
                if (item3.DataSource.StartsWith(text, StringComparison.OrdinalIgnoreCase) || item3.DataSource.StartsWith(text2, StringComparison.OrdinalIgnoreCase))
                {
                    Item item = ResolveCompositeDatasource(item3.DataSource, currentItem);
                    if (item.Paths.Path.StartsWith(compositeDatasourceChild.Paths.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        item3.DataSource = text + ResolveCompositeDatasource(item3.DataSource, currentItem).Paths.Path.Replace(compositeDatasourceChild.Paths.Path, string.Empty);
                    }
                    if (item.Paths.Path.Equals($"{currentItem.Paths.Path}/Data/{item.Name}"))
                    {
                        item3.DataSource = item3.DataSource.Replace(text, text2);
                    }
                }
                if (ID.IsID(item3.DataSource))
                {
                    Item item2 = currentItem.Database.GetItem(new ID(item3.DataSource));
                    item3.DataSource = item2.Paths.Path;
                }
                if (item3.DataSource.Equals(compositeDatasourceChild.Paths.Path))
                {
                    item3.DataSource = string.Empty;
                }
                if (item3.DataSource.StartsWith(compositeDatasourceChild.Paths.Path, StringComparison.InvariantCultureIgnoreCase))
                {
                    item3.DataSource = "local:" + item3.DataSource.Substring(compositeDatasourceChild.Paths.Path.Length);
                }
            }
        }

        protected virtual Item ResolveCompositeDatasource(string datasource, Item contextItem)
        {
            ID result;
            if (ID.TryParse(datasource, out result))
            {
                return Context.Database.Items[result];
            }
            ResolveRenderingDatasourceArgs resolveRenderingDatasourceArgs = new ResolveRenderingDatasourceArgs(datasource);
            if (contextItem != null)
            {
                resolveRenderingDatasourceArgs.CustomData.Add("contextItem", contextItem);
            }
            CorePipeline.Run("resolveRenderingDatasource", resolveRenderingDatasourceArgs);
            return Context.Database.GetItem(resolveRenderingDatasourceArgs.Datasource);
        }

        protected virtual string GetNewLayoutFromRequest(string newlayout)
        {
            string text = System.Web.HttpContext.Current.Request.Form["data"];
            if (text != null)
            {
                newlayout = JsonConvert.DeserializeObject<OnPageSaveDataModel>(text).Layout;
                newlayout = WebEditUtil.ConvertJSONLayoutToXML(newlayout);
                newlayout = newlayout.Replace("&amp;amp;", "&amp;");
            }
            return newlayout;
        }

        protected virtual void TransformPlaceholderPaths(RenderingModel compositeRendering, int index, IEnumerable<RenderingModel> renderings)
        {
            index++;
            string dynamicPlaceholderId = GetDynamicPlaceholderId(compositeRendering);
            string pattern = string.Format("(?<={0}+)-{1}-{2}", "section-[title|content]", index, dynamicPlaceholderId);
            foreach (RenderingModel rendering in renderings)
            {
                rendering.Placeholder = new Placeholder(rendering.Placeholder).GetCompositeSectionPlaceholder();
                rendering.Placeholder = Regex.Replace(rendering.Placeholder, pattern, string.Empty);
            }
        }

        protected virtual IList<RenderingModel> GetRenderingsFromCompositeSection(RenderingModel compositeRendering, int sectionIndex, IEnumerable<RenderingModel> injectedCompositeRenderings)
        {
            sectionIndex++;
            string dynamicPlaceholderId = GetDynamicPlaceholderId(compositeRendering);
            string renderingPattern = string.Format("{0}/{1}+-{2}-{3}", compositeRendering.Placeholder, "section-[title|content]", sectionIndex, dynamicPlaceholderId);
            return (from r in injectedCompositeRenderings
                    where Regex.IsMatch(r.Placeholder, renderingPattern)
                    select r).ToList();
        }

        protected string GetDynamicPlaceholderId(RenderingModel compositeRendering)
        {
            return compositeRendering.Parameters["DynamicPlaceholderId"];
        }

        protected void SetLayoutValue(LayoutModel tabItemModel, string newLayoutValue)
        {
            LayoutField layoutField = new LayoutField(tabItemModel.InnerItem);
            tabItemModel.InnerItem.Editing.BeginEdit();
            if (Registry.GetString(Sitecore.ExperienceEditor.Constants.RegistryKeys.EditAllVersions) == "on")
            {
                tabItemModel.InnerItem.Fields[FieldIDs.LayoutField].Value = newLayoutValue;
            }
            else
            {
                layoutField.Value = newLayoutValue;
            }
            tabItemModel.InnerItem.Editing.EndEdit();
        }

        protected virtual Item GetCompositeDatasource(Item contextItem, string datasource)
        {
            ResolveRenderingDatasourceArgs resolveRenderingDatasourceArgs = new ResolveRenderingDatasourceArgs(datasource);
            resolveRenderingDatasourceArgs.CustomData["contextItem"] = contextItem;
            CorePipeline.Run("resolveRenderingDatasource", resolveRenderingDatasourceArgs, failIfNotExists: false);
            return contextItem.Database.GetItem(resolveRenderingDatasourceArgs.Datasource);
        }

        protected virtual void RemoveInjectedCompositeRenderings(DeviceModel deviceModel)
        {
            deviceModel.Renderings.RenderingsCollection.RemoveAll(IsInCompositePlaceholder);
        }

        protected virtual IEnumerable<RenderingModel> GetInjectedCompositeRenderings(DeviceModel deviceModel)
        {
            return deviceModel.Renderings.RenderingsCollection.Where(IsInCompositePlaceholder);
        }

        protected virtual bool IsInCompositePlaceholder(RenderingModel r)
        {
            if (r.Placeholder != null)
            {
                return Regex.IsMatch(r.Placeholder, "section-[title|content]");
            }
            return false;
        }
    }
}