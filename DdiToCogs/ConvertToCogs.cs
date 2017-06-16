using Colectica.Cogs.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using CsvHelper;

namespace DdiToCogs
{
    public class ConvertToCogs
    {

        // all elements in DDI should have unique names, but are not in some locations across "substitution" namespaces
        Dictionary<string, Item> items = new Dictionary<string, Item>();
        Dictionary<string, DataType> dataTypes = new Dictionary<string, DataType>();
        Dictionary<string, DataType> simpleTypes = new Dictionary<string, DataType>();
        Dictionary<string, List<string>> derivedFrom = new Dictionary<string, List<string>>();

        List<TypedReference> stronglyTypedReference = new List<TypedReference>();
        List<TypedReference> conventionTypedReference = new List<TypedReference>();
        List<TypedReference> unknownTypedReference = new List<TypedReference>();

        List<TypedReference> loadedTypedReference = new List<TypedReference>();

        private string ProcessXmlSchemaAnnotation(XmlSchemaAnnotation annotation)
        {
            if (annotation == null)
            {
                return null;
            }

            string result = string.Empty;
            foreach(var doc in annotation.Items.Cast<XmlSchemaDocumentation>())
            {
                foreach(var markup in doc.Markup)
                {
                    result += markup.InnerText;
                }
            }

            if(result == string.Empty)
            {
                //TODO warn about no documentation
            }

            return result;

        }

        private void ProcessComplexType(XmlSchemaComplexType complexType, bool isItem)
        {
            DataType dataType;
            if (isItem)
            {
                dataType = new Item();
            }
            else
            {
                dataType = new DataType();
            }
            dataType.Name = complexType.QualifiedName.Name;
            dataType.IsAbstract = complexType.IsAbstract;

            // annotations are not consistent across the schemas
            dataType.Description += ProcessXmlSchemaAnnotation(complexType.Annotation);
            if (complexType.Annotation == null)
            {
                dataType.Description += ProcessXmlSchemaAnnotation(complexType?.ContentModel?.Annotation);
            }

            dataType.DeprecatedNamespace = complexType.QualifiedName.Namespace;

            // if this is an extension
            if(complexType.ContentModel != null)
            {
                if(complexType.Particle != null) { throw new Exception(); }

                

                if (complexType.ContentModel.Content is XmlSchemaSimpleContentExtension simpleContentExtension)
                {
                    dataType.Extends = simpleContentExtension.BaseTypeName.Name;
                    derivedFrom.AddValue(dataType.Extends, dataType.Name);

                    var attributeProps = GetPropertiesFromAttributes(simpleContentExtension.Attributes);
                    dataType.Properties.AddRange(attributeProps);
                }
                else if (complexType.ContentModel.Content is XmlSchemaComplexContentExtension complexContentExtension)
                {
                    dataType.Extends = complexContentExtension.BaseTypeName.Name;
                    derivedFrom.AddValue(dataType.Extends, dataType.Name);

                    if(complexContentExtension.Particle != null)
                    {
                        var elementProps = GetProperties(complexContentExtension.Particle);
                        dataType.Properties.AddRange(elementProps);
                    }
                    else
                    {
                        // Only adding attributes
                    }
                    var attributeProps = GetPropertiesFromAttributes(complexContentExtension.Attributes);
                    dataType.Properties.AddRange(attributeProps);
                    
                }
                else
                {
                    throw new Exception();
                }
            }
            else
            {
                if(complexType.ContentTypeParticle.GetType().Name == "EmptyParticle")
                {

                }
                else
                {
                    if (complexType.Particle == null) { throw new Exception(); }

                    var elementProps = GetProperties(complexType.Particle);
                    dataType.Properties.AddRange(elementProps);

                    var attributeProps = GetPropertiesFromAttributes(complexType.Attributes);
                    dataType.Properties.AddRange(attributeProps);
                }
                
            }

            


            if (isItem)
            {
                if (items.ContainsKey(dataType.Name))
                {
                    throw new InvalidOperationException("DDI items must have unique names: " + dataType.Name);
                }
                items[dataType.Name] = dataType as Item;
            }
            else
            {                
                if (dataTypes.ContainsKey(dataType.Name))
                {
                    throw new InvalidOperationException("DDI dataTypes must have unique names: " + dataType.Name);
                }
                dataTypes[dataType.Name] = dataType;                
            }


        }


        private string XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema";
        private List<Property> GetPropertiesFromAttributes(XmlSchemaObjectCollection attributes)
        {
            var result = new List<Property>();

            foreach(var schemaObject in attributes)
            {
                if(schemaObject is XmlSchemaAttribute attribute)
                {
                    Property p = new Property();
                    p.Description = ProcessXmlSchemaAnnotation(attribute.Annotation);
                    p.DataType = attribute.AttributeSchemaType.QualifiedName.Name;
                    p.Name = attribute.QualifiedName.Name;
                    if(attribute.Use == XmlSchemaUse.Optional)
                    {
                        p.MinCardinality = "0";
                    }
                    else if (attribute.Use == XmlSchemaUse.Required)
                    {
                        p.MinCardinality = "1";
                    }                    
                    p.MaxCardinality = "1";
                    

                    p.DeprecatedNamespace = attribute.QualifiedName.Namespace;
                    p.DeprecatedElementOrAttribute = "a";

                    if (attribute.UnhandledAttributes != null) { throw new Exception(); }

                    result.Add(p);
                }
                else
                {
                    //TODO handle the one attribute ref group for PRIVACY
                    Property p = new Property();
                    p.Description = "A basic set of privacy codes for the parent element. These may be stricter than the general access restrictions for the overall metadata. If available codes are insufficient this may also contain any string.";
                    p.DataType = "PrivacyCodeType";
                    p.Name = "privacy";
                    p.DeprecatedElementOrAttribute = "a";
                    p.MinCardinality = "0";
                    p.MaxCardinality = "1";

                    result.Add(p);
                }

            }

            return result;
        }
        private List<Property> GetProperties(XmlSchemaParticle particle)
        {
            var result = new List<Property>();

            if(particle is XmlSchemaSequence sequence)
            {
                foreach(var item in sequence.Items)
                {
                    if(item is XmlSchemaParticle p)
                    {
                        var partialResult = GetProperties(p);
                        result.AddRange(partialResult);
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
                
            }
            else if(particle is XmlSchemaChoice choice)
            {
                // detect choice between inline and reference
                List<XmlSchemaElement> siblings = choice.Items.OfType<XmlSchemaElement>().ToList();

                // if there is a versionable or maintainable inline, ensure there is a reference, and collapse it to a reference
                foreach(var sibling in siblings)
                {
                    XmlSchemaComplexType ct = sibling.ElementSchemaType as XmlSchemaComplexType;
                    if (ct == null) { continue; }

                    if (ct.AttributeUses.Values.Cast<XmlSchemaAttribute>().Any(a =>
                        a.Name == "isVersionable" ||
                        a.Name == "isMaintainable"))
                    {
                        if (siblings.Count == 2)
                        {
                            bool hasSiblingRef = siblings.Any(x => x.QualifiedName.Name == sibling.QualifiedName.Name + "Reference");
                            // an inline/reference pair, process only one.
                            var reference = siblings.Find(x => x.QualifiedName.Name == sibling.QualifiedName.Name + "Reference");
                            if (reference == null)
                            {
                                if(sibling.QualifiedName.Name == "BaseRecordLayout")
                                {
                                    reference = siblings.Find(x => x.QualifiedName.Name == "RecordLayoutReference");
                                }
                                if (sibling.QualifiedName.Name == "BaseLogicalProduct")
                                {
                                    reference = siblings.Find(x => x.QualifiedName.Name == "LogicalProductReference");
                                }
                            }
                            
                            string parentTypeName = null;
                            XmlSchemaComplexType complexParent = GetFirstComplexParent(choice);
                            if (complexParent == null) { throw new InvalidOperationException("Could not find the parent of this choice"); }
                            parentTypeName = GetTypeName(complexParent.QualifiedName.Name);
                            

                            string itemName = GetTypeName(ct.QualifiedName.Name);
                            if (ct.IsAbstract)
                            {
                                //TODO handle substitution groups?
                            }


                            TypedReference strongly = new TypedReference();
                            strongly.ParentTypeName = parentTypeName;
                            strongly.ReferenceName = reference.QualifiedName.Name;
                            strongly.ItemTypeName = itemName;
                            stronglyTypedReference.Add(strongly);


                            Property p = GetProperty(sibling);
                            p.Name = strongly.ReferenceName; // use the reference name
                            p.MinCardinality = choice.MinOccursString;
                            p.MaxCardinality = choice.MaxOccursString;
                            UpdateCardinality(p);
                            result.Add(p);

                            return result;

                        }
                        else
                        {
                            bool hasSiblingRef = siblings.Any(x => x.QualifiedName.Name == sibling.QualifiedName.Name + "Reference");
                            if (hasSiblingRef)
                            {
                                throw new InvalidOperationException("Inline or reference is normally a pair");
                            }
                        }
                    }
                    else
                    {

                    }

                }
                




                foreach (var item in choice.Items)
                {
                    if (item is XmlSchemaParticle p)
                    {
                        var partialResult = GetProperties(p);
                        result.AddRange(partialResult);
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }
            else if(particle is XmlSchemaElement element)
            {
                // if this is a reference, record if it is a possible type or an unknown type
                TypedReference typedReference = null;

                if (element.ElementSchemaType is XmlSchemaComplexType reference)
                {
                    if (reference.AttributeUses.Values.Cast<XmlSchemaAttribute>().Any(a => a.Name == "isReference"))
                    {
                        typedReference = new TypedReference();

                        typedReference.ReferenceName = element.QualifiedName.Name;
                        string possibleTypeName = typedReference.ReferenceName.Replace("Reference", string.Empty) + "Type";
                        if (IsSchemaType(possibleTypeName))
                        {
                            if (IsTypeReferenceable(possibleTypeName))
                            {
                                typedReference.ItemTypeName = GetTypeName(possibleTypeName);
                            }                           
                        }

                        XmlSchemaComplexType complexParent = GetFirstComplexParent(element);
                        if (complexParent == null) { throw new InvalidOperationException("Could not find the parent of this reference"); }
                        typedReference.ParentTypeName = GetTypeName(complexParent.QualifiedName.Name);

                        if(typedReference.ItemTypeName == null)
                        {
                            unknownTypedReference.Add(typedReference);
                        }
                        else
                        {
                            conventionTypedReference.Add(typedReference);
                        }
                    }
                }

                


                Property p = GetProperty(element);

                // convention based item type
                if(typedReference != null && typedReference.ItemTypeName != null)
                {
                    p.DataType = typedReference.ItemTypeName;
                }
                // pre-defined type mapping loaded
                else if(typedReference != null)
                {
                    var defined = loadedTypedReference.Where(x => 
                        x.ParentTypeName == typedReference.ParentTypeName &&
                        x.ReferenceName == typedReference.ReferenceName).FirstOrDefault();
                    if(defined != null && !string.IsNullOrWhiteSpace(defined.ItemTypeName))
                    {
                        p.DataType = defined.ItemTypeName;
                    }
                }

                result.Add(p);
            }
            else if (particle is XmlSchemaGroupRef groupRef)
            {
                // only for the dc terms inclusion
                // http://purl.org/dc/terms/:elementsAndRefinementsGroup

                // these will be special cased by cogs
                if(groupRef.RefName.Name == "elementsAndRefinementsGroup")
                {
                    Property p = new Property();
                    p.Description = ProcessXmlSchemaAnnotation(groupRef.Annotation);

                    p.DataType = "DublinCoreTerms";
                    p.Name = "DublinCoreTerms";

                    p.MinCardinality = "0";
                    p.MaxCardinality = "n";
                    p.DeprecatedElementOrAttribute = "e";
                    
                    result.Add(p);
                }
                else
                {
                    // unknown group
                }


            }
            else
            {
                throw new Exception();
            }


            return result;
        }

        private XmlSchemaComplexType GetFirstComplexParent(XmlSchemaObject item)
        {
            XmlSchemaObject parent = item;
            while (parent.Parent != null)
            {
                parent = parent.Parent;
                if (parent is XmlSchemaComplexType complexParent)
                {
                    return complexParent;
                }
            }
            return null;
        }

        private Property GetProperty(XmlSchemaElement element)
        {
            Property p = new Property();
            p.Description = ProcessXmlSchemaAnnotation(element.Annotation);

            p.DataType = element.ElementSchemaType.QualifiedName.Name;
            p.Name = element.QualifiedName.Name;

            p.MinCardinality = element.MinOccursString;
            p.MaxCardinality = element.MaxOccursString;
            UpdateCardinality(p);



            p.DeprecatedNamespace = element.QualifiedName.Namespace;
            p.DeprecatedElementOrAttribute = "e";


            if (element.UnhandledAttributes != null) { throw new Exception(); }
            return p;
        }
        
        private void UpdateCardinality(Property p)
        {
            if (p.MaxCardinality == "unbounded") { p.MaxCardinality = "n"; }
            if (p.MaxCardinality == null) { p.MaxCardinality = "1"; }
            if (p.MinCardinality == null) { p.MinCardinality = "1"; }
        }

        public string SchemaLocation { get; set; }
        public string TargetDirectory { get; set; }
        public bool Overwrite { get; set; }
        public string TypeDefinitionFile { get; set; }

        public ConvertToCogs()
        {

        }

        XmlSchemaSet ddiSchema;
        public int Convert()
        {

            if(SchemaLocation == null)
            {
                throw new InvalidOperationException("Schema location must be specified");
            }
            if(TargetDirectory == null)
            {
                throw new InvalidOperationException("Target directory must be specified");
            }

            ddiSchema = GetSchema(SchemaLocation);

            if(schemaError) { return 2; }


            // load typed property definitions
            if(!string.IsNullOrWhiteSpace(TypeDefinitionFile) && File.Exists(TypeDefinitionFile))
            {
                using(TextReader reader = File.OpenText(TypeDefinitionFile))
                {
                    var csv = new CsvReader(reader);
                    loadedTypedReference = csv.GetRecords<TypedReference>().ToList();
                }
            }



            foreach(var definedType in ddiSchema.GlobalTypes.Values.Cast<XmlSchemaType>())
            {

                // skip the wierd substitution groups which are not used
                if(definedType.QualifiedName.Namespace == "ddi:physicaldataproduct_ncube_inline:3_2" ||
                    definedType.QualifiedName.Namespace == "ddi:physicaldataproduct_ncube_normal:3_2" ||
                    definedType.QualifiedName.Namespace == "ddi:physicaldataproduct_ncube_tabular:3_2" ||
                    definedType.QualifiedName.Namespace == "ddi:physicaldataproduct_proprietary:3_2")
                {
                    continue;
                }

                // handle inclusion of DC terms within COGS itself
                if (definedType.QualifiedName.Namespace == "http://purl.org/dc/elements/1.1/" ||
                    definedType.QualifiedName.Namespace == "http://purl.org/dc/dcmitype/" ||
                    definedType.QualifiedName.Namespace == "http://purl.org/dc/terms/" )
                {
                    continue;
                }
                
                if(definedType.QualifiedName.Name == "anyType" || 
                    definedType.QualifiedName.Name == "FragmentType" ||
                    definedType.QualifiedName.Name == "FragmentInstanceType")
                {
                    continue;
                }

                XmlSchemaComplexType complexType = definedType as XmlSchemaComplexType;
                if(complexType != null)
                {


                    bool isItem = complexType.AttributeUses.Values.Cast<XmlSchemaAttribute>().Any(a =>
                                            a.Name == "isVersionable" ||
                                            a.Name == "isMaintainable");

                    ProcessComplexType(complexType, isItem);


                }

                XmlSchemaSimpleType simpleType = definedType as XmlSchemaSimpleType;
                if (simpleType != null)
                {
                    DataType dataType = new DataType();
                    dataType.Name = simpleType.QualifiedName.Name;
                    dataType.DeprecatedNamespace = simpleType.QualifiedName.Namespace;

                    // annotations are not consistent across the schemas
                    dataType.Description += ProcessXmlSchemaAnnotation(simpleType.Annotation);
                    if (simpleType.Annotation == null)
                    {
                        dataType.Description += ProcessXmlSchemaAnnotation(simpleType?.Content?.Annotation);
                    }



                    if (simpleTypes.ContainsKey(dataType.Name))
                    {
                        throw new InvalidOperationException("DDI complexTypes must have unique names: " + dataType.Name);
                    }
                    simpleTypes[dataType.Name] = dataType;

                }
            }


            WriteToCogs();

            return 0;
        }

        private bool IsSchemaType(string schemaTypeName)
        {
            var definedType = ddiSchema.GlobalTypes.Values.Cast<XmlSchemaType>().Where(x => x.QualifiedName.Name == schemaTypeName).FirstOrDefault();
            if(definedType == null)
            {
                return false;
            }
            return true;
        }

        private bool IsTypeReferenceable(string schemaTypeName)
        {
            var definedType = ddiSchema.GlobalTypes.Values.Cast<XmlSchemaType>().Where(x => x.QualifiedName.Name == schemaTypeName).FirstOrDefault();
            if (definedType == null)
            {
                return false;
            }

            if (definedType is XmlSchemaComplexType complexType)
            {
                if (complexType.AttributeUses.Values.Cast<XmlSchemaAttribute>().Any(a =>
                        a.Name == "isIdentifiable" ||
                        a.Name == "isVersionable" ||
                        a.Name == "isMaintainable"))
                {
                    return true;
                }
            }
            return false;
        }

        private string GetTypeName(string schemaTypeName)
        {
            var definedType = ddiSchema.GlobalTypes.Values.Cast<XmlSchemaType>().Where(x => x.QualifiedName.Name == schemaTypeName).FirstOrDefault();
            
            if(definedType == null) { throw new InvalidOperationException("Invalid type lookup"); }

            var complexType = definedType as XmlSchemaComplexType;
            if(complexType != null)
            {
                if (complexType.AttributeUses.Values.Cast<XmlSchemaAttribute>().Any(a =>
                        a.Name == "isVersionable" ||
                        a.Name == "isMaintainable"))
                {
                    if (schemaTypeName.EndsWith("Type"))
                    {
                        return schemaTypeName.Substring(0, schemaTypeName.LastIndexOf("Type"));
                    }
                }
            }
            return schemaTypeName;
        }

        private void WriteToCogs()
        {
            if(Overwrite && Directory.Exists(TargetDirectory))
            {
                Directory.Delete(TargetDirectory, true);
            }

            Directory.CreateDirectory(TargetDirectory);

            // write out found typed relationships
            TextWriter textWriter = new StringWriter();
            var csv = new CsvWriter(textWriter);
            csv.WriteRecords(stronglyTypedReference);
            File.WriteAllText(Path.Combine(TargetDirectory, "stronglyTypedReferences.csv"), textWriter.ToString(), Encoding.UTF8);

            textWriter = new StringWriter();
            csv = new CsvWriter(textWriter);
            csv.WriteRecords(conventionTypedReference);
            File.WriteAllText(Path.Combine(TargetDirectory, "conventionTypedReference.csv"), textWriter.ToString(), Encoding.UTF8);

            textWriter = new StringWriter();
            csv = new CsvWriter(textWriter);
            csv.WriteRecords(unknownTypedReference);
            File.WriteAllText(Path.Combine(TargetDirectory, "unknownTypedReference.csv"), textWriter.ToString(), Encoding.UTF8);


            foreach (var itemPair in items)
            {
                string itemPath = Path.Combine(TargetDirectory, GetTypeName(itemPair.Key));
                Directory.CreateDirectory(itemPath);

                Item item = itemPair.Value;
                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    File.WriteAllText(Path.Combine(itemPath, "readme.markdown"), item.Description);
                }

                textWriter = new StringWriter();
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(item.Properties);
                File.WriteAllText(Path.Combine(itemPath, GetTypeName(item.Name) + ".csv"), textWriter.ToString(), Encoding.UTF8);

                if(item.Extends != null)
                {
                    string extendsFile = Path.Combine(itemPath, "Extends." + GetTypeName(item.Extends));
                    File.Create(extendsFile).Dispose();
                }
                if (item.IsAbstract)
                {
                    string extendsFile = Path.Combine(itemPath, "Abstact");
                    File.Create(extendsFile).Dispose();
                }
            }

            string typesPath = Path.Combine(TargetDirectory, "types");
            Directory.CreateDirectory(typesPath);

            foreach (var typePair in dataTypes)
            {
                string itemPath = Path.Combine(typesPath, typePair.Key);
                Directory.CreateDirectory(itemPath);

                DataType dataType = typePair.Value;
                if (!string.IsNullOrWhiteSpace(dataType.Description))
                {
                    File.WriteAllText(Path.Combine(itemPath, "readme.markdown"), dataType.Description);
                }

                textWriter = new StringWriter();
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(dataType.Properties);
                File.WriteAllText(Path.Combine(itemPath, dataType.Name + ".csv"), textWriter.ToString(), Encoding.UTF8);

                if (dataType.Extends != null)
                {
                    string extendsFile = Path.Combine(itemPath, "Extends." + dataType.Extends);
                    File.Create(extendsFile).Dispose();
                }
                if (dataType.IsAbstract)
                {
                    string extendsFile = Path.Combine(itemPath, "Abstact");
                    File.Create(extendsFile).Dispose();
                }
            }

        }

        bool schemaError;




        public XmlSchemaSet GetSchema(string filename)
        {
            XmlSchemaSet xmlSchemaSet = new XmlSchemaSet();
            xmlSchemaSet.ValidationEventHandler += new ValidationEventHandler(ValidationCallback);
            

            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Parse;
            
            // trim the xhtml out
            string reusable = Path.GetDirectoryName(filename) + Path.DirectorySeparatorChar + "reusable.xsd";
            string tempReusable = Path.GetTempFileName();
            ReplaceXhtmlContentType(reusable, tempReusable);
            xmlSchemaSet.XmlResolver = new XmlCustomResolver() { TempReusableFile = tempReusable };

            // since there is a DTD entity in reusable, load it first
            using (XmlReader reader = XmlReader.Create(tempReusable, settings))
            {
                XmlSchema xmlSchema = XmlSchema.Read(reader, new ValidationEventHandler(ValidationCallback));
                xmlSchemaSet.Add(xmlSchema);
            }

            using (XmlReader reader = XmlReader.Create(filename, settings))
            {
                XmlSchema xmlSchema = XmlSchema.Read(reader, new ValidationEventHandler(ValidationCallback));                
                xmlSchemaSet.Add(xmlSchema);
            }

            xmlSchemaSet.Compile();

            File.Delete(tempReusable);
            return xmlSchemaSet;
        }

        void ValidationCallback(object sender, ValidationEventArgs args)
        {
            if (args.Severity == XmlSeverityType.Warning)
            {
                //
            }
            else if (args.Severity == XmlSeverityType.Error)
            {
                schemaError = true;
                Console.WriteLine(args.Message);
            }
        }

        private void ReplaceXhtmlContentType(string input, string output)
        {

            XDocument doc = XDocument.Load(input);
            XNamespace schemaNS = "http://www.w3.org/2001/XMLSchema";
            XElement import = doc.Root.Elements(schemaNS + "import").Where(x => x.Attribute("schemaLocation").Value == "ddi-xhtml11.xsd").FirstOrDefault();
            if (import != null)
            {
                import.Remove();
            }
            XElement contentType = doc.Root.Elements(schemaNS + "complexType").Where(x => x.Attribute("name").Value == "ContentType").FirstOrDefault();
            if (contentType != null)
            {
                XElement stringType = doc.Root.Elements(schemaNS + "complexType")
                    .Where(x => x.Attribute("name").Value == "StringType").FirstOrDefault();

                XElement clone = new XElement(stringType);
                clone.Attribute("name").Value = "ContentType";

                contentType.ReplaceWith(clone);
            }
            File.WriteAllText(output, doc.ToString(), Encoding.UTF8);
        }
    }
}
