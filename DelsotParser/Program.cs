using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace DelsotParser
{
    class Program
    {
        const string DELSOT_URL = "http://delsot.ru";

        static void Main()
        {
            string startPage = DELSOT_URL + "/catalog";
            HtmlWeb htmlWeb = new HtmlWeb();
            HtmlDocument mainPage = htmlWeb.Load(startPage);
            HtmlNode tableBody = mainPage.DocumentNode.SelectSingleNode("//tbody");

            IDictionary<string, ICollection<Category>> subcategoriesByCategory = new Dictionary<string, ICollection<Category>>();
            foreach (HtmlNode tableRow in tableBody.Elements("tr"))
            {
                // Убираем элементы текстовые узлы, которые между тегами.
                IEnumerable<HtmlNode> tagNodes = tableRow.ChildNodes.Where((item, index) => index % 2 == 0);

                // Убираем картинки.
                IEnumerable<HtmlNode> textCells = tagNodes.Where((item, index) => index % 2 != 0);

                // Собираем ссылки на подкатерогии.
                foreach (HtmlNode textCell in textCells)
                {
                    IEnumerable<HtmlNode> categoryAnchors = textCell.Descendants("a");

                    string supercategory = categoryAnchors.First().InnerText;

                    if (categoryAnchors.Count() > 1) // Убираем надкатегорию.
                    {
                        categoryAnchors = categoryAnchors.Where((item, index) => index > 0);
                    }

                    // Формируем список ссылок на все подкатегории.
                    ICollection<Category> categories = new LinkedList<Category>();
                    foreach (HtmlNode anchor in categoryAnchors)
                    {
                        Category newCategory = new Category();
                        newCategory.Name = anchor.InnerText;
                        newCategory.Url = anchor.Attributes["href"].Value;
                        categories.Add(newCategory);
                    }

                    subcategoriesByCategory.Add(supercategory, categories);
                }
            }

            var categoryToSubcategoriesToArticleUrls = new Dictionary<string, Dictionary<string, ICollection<Article>>>();
            foreach (string category in subcategoriesByCategory.Keys)
            {
                var articlesBySubcategory = new Dictionary<string, ICollection<Article>>();
                foreach (Category subcategory in subcategoriesByCategory[category])
                {
                    // Собираем товары по номерам страниц (10 товаров на 1 странице).
                    ICollection<string> articleUrls = new LinkedList<string>();
                    for (int i = 0; ; i++)
                    {
                        HtmlDocument categoryPage = htmlWeb.Load(DELSOT_URL + subcategory.Url + "?page=" + i);

                        // Собираем ссылки из списка товаров.
                        IEnumerable<HtmlNode> anchors = categoryPage.DocumentNode.SelectNodes("//div[@class=\"item-list\"]/ul/li//a");

                        // Если на очередной странице нет товаров, то прекращаем собирать ссылки на продукты для данной подкатегории.
                        if (anchors == null)
                            break;

                        // Если попали на страницу одиночного товара, то сохраняем его Url и 
                        // прекращаем собирать ссылки на продукты для данной подкатегории.
                        if (anchors.All(anchor => anchor.Attributes["href"].Value == "#"))
                        {
                            articleUrls.Add(subcategory.Url);
                            break;
                        }

                        // Убираем ссылки на картинки.
                        IEnumerable<HtmlNode> articleAnchors = anchors.Where((item, index) => index % 2 != 0);

                        foreach (HtmlNode articleAnchor in articleAnchors)
                        {
                            articleUrls.Add(articleAnchor.Attributes["href"].Value);
                        }
                    }

                    ICollection<Article> articles = articleUrls.Select(url => ParseArticle(url)).ToArray();
                    articlesBySubcategory.Add(subcategory.Name, articles);
                }
                categoryToSubcategoriesToArticleUrls.Add(category, articlesBySubcategory);
            }

            string json = JsonConvert.SerializeObject(categoryToSubcategoriesToArticleUrls, Formatting.Indented);
            File.WriteAllText(@"parsedDelsot.json", json);

            string applicationPath = AppDomain.CurrentDomain.BaseDirectory;
            string contentPath = applicationPath + "Delsot";
            Directory.CreateDirectory(contentPath);

            foreach (string category in categoryToSubcategoriesToArticleUrls.Keys)
            {
                string categoryPath = contentPath + @"\" + category;
                Directory.CreateDirectory(categoryPath);

                foreach (string subcategory in categoryToSubcategoriesToArticleUrls[category].Keys)
                {
                    string subcategoryPath = categoryPath + @"\" + subcategory;
                    Directory.CreateDirectory(subcategoryPath);

                    foreach (Article article in categoryToSubcategoriesToArticleUrls[category][subcategory])
                    {
                        string articlePath = subcategoryPath + @"\" + article.Name + @"\";
                        Directory.CreateDirectory(articlePath);

                        if (article.ImageUrl != null)
                        {
                            DownloadFile(article.ImageUrl, articlePath + "image.jpg");
                        }
                        
                        if (article.LayoutUrl != null)
                        {
                            DownloadFile(article.LayoutUrl, articlePath + "layout.jpg");
                        }
                        
                        foreach (var docUrl in article.DocumentUrls)
                        {
                            if (docUrl != null)
                            {
                                if (docUrl.EndsWith(".doc"))
                                {
                                    DownloadFile(docUrl, articlePath + "document.doc");
                                }
                                else if (docUrl.EndsWith(".pdf"))
                                {
                                    DownloadFile(docUrl, articlePath + "document.pdf");
                                }
                                else
                                {
                                    DownloadFile(docUrl, articlePath + "document.jpg");
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Parsed!");
            Console.ReadKey();
        }

        public static void DownloadFile(string url, string path)
        {
            Thread.Sleep(100);
            using (WebClient client = new WebClient())
            {
                client.DownloadFile(url, path);
            }
        }

        public static Article ParseArticle(string articleUrl)
        {
            HtmlWeb htmlWeb = new HtmlWeb();
            HtmlDocument articlePage = htmlWeb.Load(DELSOT_URL + articleUrl);
            Article article = new Article();

            article.Name = articlePage.DocumentNode.SelectSingleNode("//h1").InnerText.Trim();
            article.ImageUrl = articlePage.GetElementbyId("field-slideshow-1-wrapper").SelectSingleNode("*//a/img").Attributes["src"].Value;

            // Парсим характеристики товара.
            HtmlNodeCollection tdNodes = articlePage.GetElementbyId("container_tab1").SelectNodes("*//td");
            if (tdNodes != null)
            {
                string[] data = tdNodes.Select(td => td.InnerText.Trim()).ToArray();
                for (int i = 0; i < data.Length; i += 2)
                {
                    string key = data[i];
                    string value = "";
                    if (i + 1 < data.Length)
                    {
                        value = data[i + 1];
                    }
                    if (!article.Data.ContainsKey(key))
                    {
                        article.Data.Add(key, value);
                    }
                }
            }

            article.Description = articlePage.GetElementbyId("container_tab2").SelectSingleNode("*//div[@class=\"field-item even\"]").InnerHtml;
            article.LayoutUrl = articlePage.GetElementbyId("container_tab3").SelectSingleNode("*//img").Attributes["src"].Value;

            // Сохраняем ссылки на документы.
            HtmlNodeCollection anchors = articlePage.GetElementbyId("container_tab4").SelectNodes("*//a");
            if (anchors != null)
            {
                foreach (HtmlNode anchor in anchors)
                {
                    article.DocumentUrls.Add(anchor.Attributes["href"].Value);
                }
            }

            return article;
        }
    }
}
