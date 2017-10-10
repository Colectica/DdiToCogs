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
using System.Threading;

namespace DdiToCogs
{
    public class ConvertToCogs
    {

        // all elements in DDI should have unique names, but are not in some locations across "substitution" namespaces
        Dictionary<string, Item> items = new Dictionary<string, Item>();
        Dictionary<string, DataType> dataTypes = new Dictionary<string, DataType>();
        Dictionary<string, Property> simpleTypes = new Dictionary<string, Property>();
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
            dataType.Name = GetTypeName(complexType.QualifiedName);
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
                    dataType.Extends = GetTypeName(simpleContentExtension.BaseTypeName);
                    derivedFrom.AddValue(dataType.Extends, dataType.Name);

                    var attributeProps = GetPropertiesFromAttributes(simpleContentExtension.Attributes);
                    dataType.Properties.AddRange(attributeProps);
                }
                else if (complexType.ContentModel.Content is XmlSchemaComplexContentExtension complexContentExtension)
                {
                    dataType.Extends = GetTypeName(complexContentExtension.BaseTypeName);
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

        private string ProcessSchemaTypeToSimpleType(string schemaType)
        {
            switch (schemaType)
            {
                case "integer":
                    return "int";
                case "NMTOKENS":
                case "NMTOKEN":
                case "anySimpleType":
                    return "string";
                case "string":
                case "boolean":
                case "decimal":
                case "float":
                case "double":
                case "duration":
                case "dateTime":
                case "time":
                case "date":
                case "gYearMonth":
                case "gYear":
                case "gMonthDay":
                case "gDay":
                case "gMonth":
                case "anyURI":
                case "language":
                case "nonPositiveInteger":
                case "negativeInteger":
                case "long":
                case "int":
                case "nonNegativeInteger":
                case "unsignedLong":
                case "positiveInteger":
                case "cogsDate":
                    return schemaType;
                default:
                    return null;
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
                    if(attribute.QualifiedName.Name == "space") { continue; }

                    Property p = new Property();
                    p.Description = ProcessXmlSchemaAnnotation(attribute.Annotation);
                    p.DataType = GetTypeName(attribute.AttributeSchemaType.QualifiedName);
                    
                    p.Name = attribute.QualifiedName.Name;
                    p.MinCardinality = "0";
                    if (attribute.Use == XmlSchemaUse.Optional)
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


                    if (attribute.AttributeSchemaType.QualifiedName.Name == "NMTOKENS")
                    {
                        p.Pattern = "\\c+";
                    }
                    if(attribute.AttributeSchemaType.Content is XmlSchemaSimpleTypeRestriction restrictions)
                    {
                        if(restrictions.Facets.Count > 0)
                        {
                            ProcessSimpleTypeFacets(restrictions, p);
                        }
                        
                    }


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
                            parentTypeName = GetTypeName(complexParent.QualifiedName);
                            

                            string itemName = GetTypeName(ct.QualifiedName);
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
                            //p.DataType = GetTypeName(p.DataType);// this is always a versionable+
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
                        typedReference.ParentTypeName = GetTypeName(complexParent.QualifiedName);

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
                        // references to complex types/historic identifiable refs have Type at the end
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

                    p.DataType = "dcTerms";
                    p.Name = "DcTerms";

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
            
            p.DataType = GetTypeName(element.ElementSchemaType.QualifiedName);

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
            if (p.MinCardinality == null) { p.MinCardinality = "0"; }
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


            // inject dublin core type


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
                
                if(definedType.QualifiedName.Name == "FragmentType" ||
                    definedType.QualifiedName.Name == "FragmentInstanceType")
                {
                    continue;
                }

                if (definedType.QualifiedName.Name == "anyType")
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
                    Property property = new Property();
                    property.Name = simpleType.QualifiedName.Name;
                    property.DeprecatedNamespace = simpleType.QualifiedName.Namespace;

                    // annotations are not consistent across the schemas
                    property.Description += ProcessXmlSchemaAnnotation(simpleType.Annotation);
                    if (simpleType.Annotation == null)
                    {
                        property.Description += ProcessXmlSchemaAnnotation(simpleType?.Content?.Annotation);
                    }

                    if(simpleType.Content is XmlSchemaSimpleTypeRestriction simpleTypeRestriction)
                    {
                        if (simpleType.TypeCode == XmlTypeCode.String)
                        {
                            property.DataType = "string";
                            ProcessSimpleTypeFacets(simpleTypeRestriction, property);
                        }
                        else if (simpleType.TypeCode == XmlTypeCode.NmToken)
                        {
                            property.DataType = "string";
                            ProcessSimpleTypeFacets(simpleTypeRestriction, property);
                        }
                        else if (simpleType.TypeCode == XmlTypeCode.Decimal)
                        {
                            property.DataType = "decimal";
                            ProcessSimpleTypeFacets(simpleTypeRestriction, property);
                        }
                        else if (simpleType.TypeCode == XmlTypeCode.Float)
                        {
                            property.DataType = "float";
                            ProcessSimpleTypeFacets(simpleTypeRestriction, property);
                        }
                        else
                        {
                            throw new InvalidOperationException("DDI simpleTypes using unknown typecode: " + simpleType.TypeCode.ToString());
                        }
                    }
                    else if (simpleType.Content is XmlSchemaSimpleTypeUnion unionType)
                    {
                        if(property.Name == "DDIURNType")
                        {
                            property.DataType = "string";
                            ProcessSimpleTypeFacets((XmlSchemaSimpleTypeRestriction)unionType.BaseMemberTypes[0].Content, property);
                        }
                        else if(property.Name == "BaseDateType")
                        {
                            property.DataType = "cogsDate";
                        }
                        else if(property.Name == "PrivacyCodeType")
                        {
                            // this is either a string, or a enumerated list. Make this a CV in a future version
                            property.DataType = "string";
                            ProcessSimpleTypeFacets((XmlSchemaSimpleTypeRestriction)unionType.BaseMemberTypes[1].Content, property);
                        }
                        else
                        {
                            throw new InvalidOperationException("DDI simpleTypes using unknown union: " + simpleType.TypeCode.ToString());
                        }
                    }
                    else if (simpleType.Content is XmlSchemaSimpleTypeList listType)
                    {
                        if (property.Name == "LanguageList")
                        {
                            property.DataType = "language";
                            property.MinCardinality = "0";
                            property.MaxCardinality = "n";
                        }
                        else
                        {
                            throw new InvalidOperationException("DDI simpleTypes using unknown typelist: " + simpleType.TypeCode.ToString());
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("No SimpleTypeRestriction for simpleType using unknown typecode: " + simpleType.TypeCode.ToString());
                    }



                    if (simpleTypes.ContainsKey(property.Name))
                    {
                        throw new InvalidOperationException("DDI simpleTypes must have unique names: " + property.Name);
                    }
                    simpleTypes[property.Name] = property;

                }
            }
                        

            ConvertAnyTypeToString();

            ConvertPropertiesToPascalCase();

            ConvertRestrictionsToSimpleTypes();

            ExpandPropertyReferencesWithoutCommonBaseClass();

            CleanUp();

            WriteToCogs();

            return 0;
        }

        private void ProcessSimpleTypeFacets(XmlSchemaSimpleTypeRestriction simpleTypeRestriction, Property property)
        {
            foreach (var facet in simpleTypeRestriction.Facets)
            {
                if (facet is XmlSchemaPatternFacet pattern)
                {
                    property.Pattern = pattern.Value;
                }
                else if (facet is XmlSchemaMinLengthFacet minLength)
                {
                    property.MinLength = int.Parse(minLength.Value);
                }
                else if (facet is XmlSchemaMaxLengthFacet maxLength)
                {
                    property.MaxLength = int.Parse(maxLength.Value);
                }
                else if (facet is XmlSchemaMinExclusiveFacet minEx)
                {
                    property.MinExclusive = int.Parse(minEx.Value);
                }
                else if (facet is XmlSchemaMaxExclusiveFacet maxEx)
                {
                    property.MaxExclusive = int.Parse(maxEx.Value);
                }
                else if (facet is XmlSchemaMinInclusiveFacet minIn)
                {
                    property.MinInclusive = int.Parse(minIn.Value);
                }
                else if (facet is XmlSchemaMaxInclusiveFacet maxIn)
                {
                    property.MaxInclusive = int.Parse(maxIn.Value);
                }
                else if(facet is XmlSchemaEnumerationFacet enumeration)
                {
                    if(enumeration.Value.Contains(" "))
                    {
                        throw new InvalidOperationException("A simpleType enumeration contains a space");
                    }

                    if (string.IsNullOrWhiteSpace(property.Enumeration))
                    {
                        property.Enumeration = enumeration.Value;
                    }
                    else
                    {
                        property.Enumeration += " " + enumeration.Value;
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unknown DDI simpleType facet for string");
                }
            }
        }

        public void ConvertPropertiesToPascalCase()
        {
            var dataTypeProperties = dataTypes.SelectMany(x => x.Value.Properties);
            var itemProperties = items.SelectMany(x => x.Value.Properties);

            foreach (var property in dataTypeProperties.Concat(itemProperties))
            {
                if (char.IsLower(property.Name[0]))
                {
                    property.Name = char.ToUpper(property.Name[0]) + property.Name.Substring(1); 
                }
            }
        }

        public void ConvertAnyTypeToString()
        {
            var dataTypeProperties = dataTypes.SelectMany(x => x.Value.Properties);
            var itemProperties = items.SelectMany(x => x.Value.Properties);

            foreach (var property in dataTypeProperties.Concat(itemProperties))
            {
                if (property.DataType == "anyType")
                {
                    property.DataType = "string";
                }
            }
        }

        public void ExpandPropertyReferencesWithoutCommonBaseClass()
        {
            foreach(var item in items.Values.Concat(dataTypes.Values))
            {
                for(int i = 0; i < item.Properties.Count; ++i)
                {
                    var property = item.Properties[i];
                    if(property.DataType.Contains(" "))
                    {
                        var datatypes = property.DataType.Split(' ');
                        item.Properties.RemoveAt(i);

                        foreach(var datatype in datatypes)
                        {
                            Property exploded = property.Clone() as Property;
                            exploded.Name = exploded.Name + "_" + datatype;
                            exploded.DataType = datatype;
                            item.Properties.Insert(i, exploded);
                        }
                    }
                }
            }            
        }

        public void ConvertRestrictionsToSimpleTypes()
        {
            var dataTypeProperties = dataTypes.SelectMany(x => x.Value.Properties);
            var itemProperties = items.SelectMany(x => x.Value.Properties);

            foreach(var property in dataTypeProperties.Concat(itemProperties))
            {
                if(simpleTypes.TryGetValue(property.DataType, out Property simpleType))
                {
                    property.DataType = simpleType.DataType;

                    // simple string restrictions
                    property.MinLength = simpleType.MinLength;
                    property.MaxLength = simpleType.MaxLength;
                    property.Enumeration = simpleType.Enumeration;
                    property.Pattern = simpleType.Pattern;
                    // numeric restrictions
                    property.MinInclusive = simpleType.MinInclusive;
                    property.MinExclusive = simpleType.MinExclusive;
                    property.MaxInclusive = simpleType.MaxInclusive;
                    property.MaxExclusive = simpleType.MaxExclusive;

                    if(simpleType.MinCardinality != null)
                    {
                        property.MinCardinality = simpleType.MinCardinality;
                    }
                    if (simpleType.MaxCardinality != null)
                    {
                        property.MaxCardinality = simpleType.MaxCardinality;
                    }
                }
            }
        }

        public void CleanUp()
        {
            var maintainable = items["Maintainable"];
            var versionable = items["Versionable"];
            var identifiable = dataTypes["IdentifiableType"];

            var abstractMaintainable = dataTypes["AbstractMaintainableType"];
            var abstractVersionable = dataTypes["AbstractVersionableType"];
            var abstractIdentifiable = dataTypes["AbstractIdentifiableType"];
            
            // add userid to versionable, identification will be injected
            var userId = abstractIdentifiable.Properties.Where(x => x.Name == "UserID").FirstOrDefault();
            versionable.Properties.Insert(0, userId);
            versionable.Extends = null;
            // keep the first 5 properties
            identifiable.Properties = identifiable.Properties.Take(5).ToList();

            maintainable.Extends = "Versionable";

            dataTypes.Remove("AbstractIdentifiableType");
            dataTypes.Remove("AbstractMaintainableType");
            dataTypes.Remove("AbstractVersionableType");

         
            // duplicate properties on one item
            var datetype = dataTypes["DateType"];
            datetype.Properties.RemoveAt(datetype.Properties.FindLastIndex(x => x.Name == "EndDate"));
            datetype.Properties.RemoveAt(datetype.Properties.FindLastIndex(x => x.Name == "HistoricalEndDate"));

            // duplicate property names across items
            var dataTypeProperties = dataTypes.SelectMany(x => x.Value.Properties).ToList();
            var itemProperties = items.SelectMany(x => x.Value.Properties).ToList();
            var allProperties = dataTypeProperties.Concat(itemProperties).ToList();
            
            var dupes2 = allProperties.Where(x => x.Name == "CodeListName").ToList();
            foreach(var prop in dupes2)
            {
                if(prop.DataType != "NameType") { prop.Name += "_string"; }
            }

            var dupes = allProperties.Where(x => x.Name == "ArrayBase");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "int") { prop.Name += "_string"; }
            }

            dupes = allProperties.Where(x => x.Name == "Interval");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "int") { prop.Name += "_IntervalType"; }
            }

            dupes = allProperties.Where(x => x.Name == "RecordLayoutReference");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "RecordLayout") { prop.DataType = "RecordLayout"; }//use the other type
            }

            dupes = allProperties.Where(x => x.Name == "Value");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "ValueType") { prop.Name += "_string"; }
            }

            dupes = allProperties.Where(x => x.Name == "CodeReference");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "CodeType") { prop.Name += "_string"; }
            }

            dupes = allProperties.Where(x => x.Name == "StartDate");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "cogsDate") { prop.Name += "_dateTime"; }
            }

            dupes = allProperties.Where(x => x.Name == "Anchor");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "AnchorType") { prop.Name += "_string"; }
            }

            dupes = allProperties.Where(x => x.Name == "LevelName");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "NameType") { prop.Name += "_string"; }
            }

            dupes = allProperties.Where(x => x.Name == "DefaultValue");
            foreach (var prop in dupes)
            {
                if (prop.DataType != "ValueType") { prop.Name += "_string"; }
            }

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

        private string GetTypeName(XmlQualifiedName qname)
        {
            string schemaTypeName = qname.Name;
            
            return GetTypeName(schemaTypeName);
        }
        private string GetTypeName(string schemaTypeName)
        {
            var definedType = ddiSchema.GlobalTypes.Values.Cast<XmlSchemaType>().Where(x => x.QualifiedName.Name == schemaTypeName).FirstOrDefault();
            
            if(definedType == null)
            {
                string simpleType = ProcessSchemaTypeToSimpleType(schemaTypeName);
                if(simpleType == null)
                {
                    throw new InvalidOperationException("Invalid type lookup");
                }
                return simpleType;
            }

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
                Thread.Sleep(50);
            }

            Directory.CreateDirectory(TargetDirectory);

            // write out found typed relationships
            TextWriter textWriter = null;
            CsvWriter csv = null;
            using (textWriter = new StringWriter())
            {
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(stronglyTypedReference);
                File.WriteAllText(Path.Combine(TargetDirectory, "stronglyTypedReferences.csv"), textWriter.ToString(), Encoding.UTF8);
            }


            using (textWriter = new StringWriter())
            {
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(conventionTypedReference);
                File.WriteAllText(Path.Combine(TargetDirectory, "conventionTypedReference.csv"), textWriter.ToString(), Encoding.UTF8);
            }


            using (textWriter = new StringWriter())
            {
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(unknownTypedReference);
                File.WriteAllText(Path.Combine(TargetDirectory, "unknownTypedReference.csv"), textWriter.ToString(), Encoding.UTF8);
            }

            HashSet<string> dataTypesUsed = new HashSet<string>();

            string settingsPath = Path.Combine(TargetDirectory, "Settings");
            Directory.CreateDirectory(settingsPath);
            
            using (textWriter = new StringWriter())
            {
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(identification);
                File.WriteAllText(Path.Combine(settingsPath, "Identification.csv"), textWriter.ToString(), Encoding.UTF8);
            }

            using (textWriter = new StringWriter())
            {
                csv = new CsvWriter(textWriter);
                csv.WriteRecords(settings);
                File.WriteAllText(Path.Combine(settingsPath, "Settings.csv"), textWriter.ToString(), Encoding.UTF8);
            }

            foreach (var itemPair in items)
            {
                string itemPath = Path.Combine(TargetDirectory, "ItemTypes", itemPair.Key);
                Directory.CreateDirectory(itemPath);

                Item item = itemPair.Value;
                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    File.WriteAllText(Path.Combine(itemPath, "readme.markdown"), item.Description);
                }

                using (textWriter = new StringWriter())
                {
                    csv = new CsvWriter(textWriter);
                    csv.WriteRecords(item.Properties);
                    File.WriteAllText(Path.Combine(itemPath, item.Name + ".csv"), textWriter.ToString(), Encoding.UTF8);
                }

                if(item.Extends != null)
                {
                    string extendsFile = Path.Combine(itemPath, "Extends." + item.Extends);
                    File.Create(extendsFile).Dispose();
                }
                if (item.IsAbstract)
                {
                    string extendsFile = Path.Combine(itemPath, "Abstact");
                    File.Create(extendsFile).Dispose();
                }

                foreach(var prop in item.Properties)
                {
                    if(items.Keys.Contains(prop.DataType)) { continue; }
                    dataTypesUsed.Add(prop.DataType);
                    dataTypesUsed.UnionWith(GetParentTypes(prop.DataType));
                }
                
            }

            string typesPath = Path.Combine(TargetDirectory, "CompositeTypes");
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

                using (textWriter = new StringWriter())
                {
                    csv = new CsvWriter(textWriter);
                    csv.WriteRecords(dataType.Properties);
                    File.WriteAllText(Path.Combine(itemPath, dataType.Name + ".csv"), textWriter.ToString(), Encoding.UTF8);
                }

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

                dataTypesUsed.UnionWith(dataType.Properties.Select(x => x.DataType));
                dataTypesUsed.UnionWith(GetParentTypes(dataType.Name));
            }

            // Topics are hard coded, to create an example
            string topicsPath = Path.Combine(TargetDirectory, "Topics");
            Directory.CreateDirectory(topicsPath);
            File.WriteAllText(Path.Combine(topicsPath, "index.txt"), "All Content Items");

            string allContentPath = Path.Combine(topicsPath, "All Content Items");
            Directory.CreateDirectory(allContentPath);
            File.Create(Path.Combine(allContentPath, "readme.markdown")).Dispose();
            var usedItemList = items.Where(x => !x.Value.IsAbstract).Select(x => x.Key);
            File.WriteAllLines(Path.Combine(allContentPath, "items.txt"), usedItemList);

            // find reusable data types that are not used or base classes of a used datatype

            var notUsed = dataTypes.Keys.Except(dataTypesUsed).ToList();
        }

        bool schemaError;


        private List<string> GetParentTypes(string dataTypeName)
        {
            if(dataTypes.TryGetValue(dataTypeName, out DataType value))
            {
                if (string.IsNullOrWhiteSpace(value.Extends))
                {
                    return new List<string>();
                }
                var result = new List<string>();
                result.Add(value.Extends);
                result.AddRange(GetParentTypes(value.Extends));
                return result;
            }
            return new List<string>();
        }


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

        List<Setting> settings = new List<Setting>()
        {
            new Setting() {Key="Title", Value="DDI Data Documentation"},
            new Setting() {Key="ShortTitle", Value="DDI"},
            new Setting() {Key="Slug", Value="ddi"},
            new Setting() {Key="Description", Value="The Data Documentation Initiative (DDI) is an international standard for describing the data produced by surveys and other observational methods in the social, behavioral, economic, and health sciences. DDI is a free standard that can document and manage different stages in the research data lifecycle, such as conceptualization, collection, processing, distribution, discovery, and archiving. Documenting data with DDI facilitates understanding, interpretation, and use -- by people, software systems, and computer networks."},
            new Setting() {Key="NamespaceUrl", Value="http://ddialliance.org/ddi"},
            new Setting() {Key="NamespacePrefix", Value="ddi"}
        };

        List<Property> identification = new List<Property>() {
            new Property()
            {
            Name = "URN",
                DataType = "string",
                MinCardinality = "1",
                MaxCardinality = "1",
                Pattern = @"[Uu][Rr][Nn]:[Dd][Dd][Ii]:[a-zA-Z0-9\-]{1,63}(\.[a-zA-Z0-9\-]{1,63})*:[A-Za-z0-9\*@$\-_]+(\.[A-Za-z0-9\*@$\-_]+)?:[0-9]+(\.[0-9]+)*",
            },
            new Property()
            {
                Name = "Agency",
                DataType = "string",
                MinCardinality = "1",
                MaxCardinality = "1",
                Pattern = @"[a-zA-Z0-9\-]{1,63}(\.[a-zA-Z0-9\-]{1,63})*",
                MinLength = 1,
                MaxLength = 253
            },
            new Property()
            {
                Name = "ID",
                DataType = "string",
                MinCardinality = "1",
                MaxCardinality = "1",
                Pattern = @"[A-Za-z0-9\*@$\-_]+(\.[A-Za-z0-9\*@$\-_]+)?"
            },
            new Property()
            {
                Name = "Version",
                DataType = "string",
                MinCardinality = "1",
                MaxCardinality = "1",
                Pattern = @"[0-9]+(\.[0-9]+)*"
            }
        };
    }
}
