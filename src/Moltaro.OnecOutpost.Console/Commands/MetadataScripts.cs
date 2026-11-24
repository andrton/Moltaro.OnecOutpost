namespace Moltaro.OnecOutpost.Console.Commands;

// The one script that metadata runs in a base as Reads, as CheckScripts holds check's, in the English variant of the 1C
// language and within what the compatibility mode 8.2.13 allows, without a procedure, Find and Mid where a later mode
// has StrFind and StrSplit. Its parameter read chooses what it reads: census, the configuration and the count of each of
// its fifteen categories; page, the objects of a category from an index with their fields and, when asked, their
// storage by GetDBStorageStructureInfo with the DBMS's names; common, the storage tables of no object of those
// categories. The S10 brief, "Internal of S10c", gives its outline and the form of what it gives:
// - census: {"platform", "configuration": {"name", "version", "compatibilityMode"}, "categories": [{"category",
//   "count"}]}, the fifteen categories in the report's order;
// - page, with category, from, count and storage: {"objects": [...], "storageError"}, each object with name, fullName,
//   synonym, comment, properties, fields, values, storage and error, read in a Try of its own whose ErrorDescription is
//   its error; storage its tables when storage is true and null otherwise, and storageError the raise of the storage,
//   which leaves every storage null and every error as it was;
// - common: the storage tables whose object, the Metadata of a table up to its second dot, is empty or no object of
//   the fifteen categories, such as a sequence's; a raise fails the Read.
// The columns of the storage structure follow the language of the session, so each is found by its English name or
// else by its Russian one. A page attaches each table to the object of its key, and a table of no object of the page
// raises. A field names each of its types: String, Number, Date, Boolean, ValueStorage and UUID by those names, which
// do not depend on the session's language, a reference by the full name of its object, any other by its presentation;
// its qualifiers, and every enumeration of a property, by XMLString. Not checked here: whether 1C allows each call to
// the read account in safe mode, which the lab proves.
internal static class MetadataScripts
{
    public const string Walk = """
        Asked = Parameters["read"];

        If Asked <> "census" And Asked <> "page" And Asked <> "common" Then
            Raise "The walk of metadata reads census, page or common.";
        EndIf;

        Categories = New Array;
        Categories.Add("Catalogs");
        Categories.Add("Documents");
        Categories.Add("Enums");
        Categories.Add("Constants");
        Categories.Add("InformationRegisters");
        Categories.Add("AccumulationRegisters");
        Categories.Add("AccountingRegisters");
        Categories.Add("CalculationRegisters");
        Categories.Add("ChartsOfCharacteristicTypes");
        Categories.Add("ChartsOfAccounts");
        Categories.Add("ChartsOfCalculationTypes");
        Categories.Add("BusinessProcesses");
        Categories.Add("Tasks");
        Categories.Add("ExchangePlans");
        Categories.Add("CommonAttributes");

        Counts = New Array;
        FullNames = New Map;
        PageCollection = Undefined;
        Objects = New Array;
        ByFullName = New Map;
        MetadataObjects = New Array;
        StorageError = Undefined;
        Common = New Array;

        For Each Category In Categories Do
            If Asked = "page" And Category <> Parameters["category"] Then
                Continue;
            EndIf;

            If Category = "Catalogs" Then
                Collection = Metadata.Catalogs;
            ElsIf Category = "Documents" Then
                Collection = Metadata.Documents;
            ElsIf Category = "Enums" Then
                Collection = Metadata.Enums;
            ElsIf Category = "Constants" Then
                Collection = Metadata.Constants;
            ElsIf Category = "InformationRegisters" Then
                Collection = Metadata.InformationRegisters;
            ElsIf Category = "AccumulationRegisters" Then
                Collection = Metadata.AccumulationRegisters;
            ElsIf Category = "AccountingRegisters" Then
                Collection = Metadata.AccountingRegisters;
            ElsIf Category = "CalculationRegisters" Then
                Collection = Metadata.CalculationRegisters;
            ElsIf Category = "ChartsOfCharacteristicTypes" Then
                Collection = Metadata.ChartsOfCharacteristicTypes;
            ElsIf Category = "ChartsOfAccounts" Then
                Collection = Metadata.ChartsOfAccounts;
            ElsIf Category = "ChartsOfCalculationTypes" Then
                Collection = Metadata.ChartsOfCalculationTypes;
            ElsIf Category = "BusinessProcesses" Then
                Collection = Metadata.BusinessProcesses;
            ElsIf Category = "Tasks" Then
                Collection = Metadata.Tasks;
            ElsIf Category = "ExchangePlans" Then
                Collection = Metadata.ExchangePlans;
            Else
                Collection = Metadata.CommonAttributes;
            EndIf;

            If Asked = "census" Then
                Counts.Add(New Structure("category, count", Category, Collection.Count()));
            ElsIf Asked = "common" Then
                For Each Item In Collection Do
                    FullNames.Insert(Item.FullName(), True);
                EndDo;
            Else
                PageCollection = Collection;
            EndIf;
        EndDo;

        If Asked = "page" Then
            PageCategory = Parameters["category"];

            If PageCollection = Undefined Then
                Raise "The walk of metadata reads no category " + String(PageCategory) + ".";
            EndIf;

            If PageCategory = "Constants" Or PageCategory = "CommonAttributes" Then
                FieldCollections = "Value";
            ElsIf PageCategory = "Enums" Then
                FieldCollections = "";
            ElsIf PageCategory = "InformationRegisters" Or PageCategory = "AccumulationRegisters" Or PageCategory = "AccountingRegisters" Or PageCategory = "CalculationRegisters" Then
                FieldCollections = "StandardAttributes,Dimensions,Resources,Attributes";
            ElsIf PageCategory = "ChartsOfAccounts" Then
                FieldCollections = "StandardAttributes,Attributes,AccountingFlags,ExtDimensionAccountingFlags,TabularSections";
            ElsIf PageCategory = "Tasks" Then
                FieldCollections = "StandardAttributes,Attributes,AddressingAttributes,TabularSections";
            Else
                FieldCollections = "StandardAttributes,Attributes,TabularSections";
            EndIf;

            Last = Min(Parameters["from"] + Parameters["count"], PageCollection.Count()) - 1;

            For Index = Parameters["from"] To Last Do
                Item = New Structure("name, fullName, synonym, comment, properties, fields, values, storage, error");
                Item.properties = New Structure;
                Item.fields = New Array;
                Item.values = New Array;
                Objects.Add(Item);

                Try
                    MetadataObject = PageCollection.Get(Index);
                    MetadataObjects.Add(MetadataObject);
                    Item.name = MetadataObject.Name;
                    Item.fullName = MetadataObject.FullName();
                    ByFullName.Insert(Item.fullName, Item);
                    Item.synonym = MetadataObject.Synonym;
                    Item.comment = MetadataObject.Comment;

                    Properties = New Structure;

                    If PageCategory = "Catalogs" Then
                        Properties.Insert("hierarchical", MetadataObject.Hierarchical);
                        Properties.Insert("hierarchyType", MetadataObject.HierarchyType);
                        Properties.Insert("codeLength", MetadataObject.CodeLength);
                        Properties.Insert("descriptionLength", MetadataObject.DescriptionLength);
                        Properties.Insert("owners", MetadataObject.Owners);
                    ElsIf PageCategory = "Documents" Then
                        Properties.Insert("numberLength", MetadataObject.NumberLength);
                        Properties.Insert("numberType", MetadataObject.NumberType);
                        Properties.Insert("numberPeriodicity", MetadataObject.NumberPeriodicity);
                        Properties.Insert("posting", MetadataObject.Posting);
                        Properties.Insert("registerRecords", MetadataObject.RegisterRecords);
                    ElsIf PageCategory = "InformationRegisters" Then
                        Properties.Insert("periodicity", MetadataObject.InformationRegisterPeriodicity);
                        Properties.Insert("writeMode", MetadataObject.WriteMode);
                    ElsIf PageCategory = "AccumulationRegisters" Then
                        Properties.Insert("registerType", MetadataObject.RegisterType);
                    ElsIf PageCategory = "ChartsOfCharacteristicTypes" Or PageCategory = "ChartsOfAccounts" Or PageCategory = "ChartsOfCalculationTypes" Or PageCategory = "ExchangePlans" Then
                        Properties.Insert("codeLength", MetadataObject.CodeLength);
                        Properties.Insert("descriptionLength", MetadataObject.DescriptionLength);
                    ElsIf PageCategory = "BusinessProcesses" Then
                        Properties.Insert("numberLength", MetadataObject.NumberLength);
                    ElsIf PageCategory = "Tasks" Then
                        Properties.Insert("numberLength", MetadataObject.NumberLength);
                        Properties.Insert("descriptionLength", MetadataObject.DescriptionLength);
                    EndIf;

                    For Each Property In Properties Do
                        If Property.Key = "owners" Or Property.Key = "registerRecords" Then
                            Names = New Array;

                            For Each Listed In Property.Value Do
                                Names.Add(Listed.FullName());
                            EndDo;

                            Item.properties.Insert(Property.Key, Names);
                        ElsIf TypeOf(Property.Value) = Type("Number") Or TypeOf(Property.Value) = Type("Boolean") Then
                            Item.properties.Insert(Property.Key, Property.Value);
                        Else
                            Item.properties.Insert(Property.Key, XMLString(Property.Value));
                        EndIf;
                    EndDo;

                    Sources = New Array;
                    Rest = FieldCollections + ",";

                    While Rest <> "" Do
                        Comma = Find(Rest, ",");
                        CollectionName = Left(Rest, Comma - 1);
                        Rest = Mid(Rest, Comma + 1);

                        If CollectionName = "Value" Then
                            Sources.Add(New Structure("collection, tabularSection, name, member", "Value", Undefined, "Value", MetadataObject));
                        ElsIf CollectionName = "TabularSections" Then
                            For Each Section In MetadataObject.TabularSections Do
                                For Each Member In Section.Attributes Do
                                    Sources.Add(New Structure("collection, tabularSection, name, member", "Attributes", Section.Name, Member.Name, Member));
                                EndDo;
                            EndDo;
                        ElsIf CollectionName <> "" Then
                            If CollectionName = "StandardAttributes" Then
                                Members = MetadataObject.StandardAttributes;
                            ElsIf CollectionName = "Attributes" Then
                                Members = MetadataObject.Attributes;
                            ElsIf CollectionName = "Dimensions" Then
                                Members = MetadataObject.Dimensions;
                            ElsIf CollectionName = "Resources" Then
                                Members = MetadataObject.Resources;
                            ElsIf CollectionName = "AccountingFlags" Then
                                Members = MetadataObject.AccountingFlags;
                            ElsIf CollectionName = "ExtDimensionAccountingFlags" Then
                                Members = MetadataObject.ExtDimensionAccountingFlags;
                            Else
                                Members = MetadataObject.AddressingAttributes;
                            EndIf;

                            For Each Member In Members Do
                                Sources.Add(New Structure("collection, tabularSection, name, member", CollectionName, Undefined, Member.Name, Member));
                            EndDo;
                        EndIf;
                    EndDo;

                    For Each Source In Sources Do
                        Member = Source.member;
                        Field = New Structure("collection, tabularSection, name, synonym, indexing, types, string, number, date",
                            Source.collection, Source.tabularSection, Source.name, Member.Synonym);

                        Try
                            Field.Insert("indexing", XMLString(Member.Indexing));
                        Except
                        EndTry;

                        TypeNames = New Array;
                        FieldType = Member.Type;

                        For Each ItemType In FieldType.Types() Do
                            If ItemType = Type("String") Then
                                TypeNames.Add("String");
                                Field.Insert("string", New Structure("length, allowedLength",
                                    FieldType.StringQualifiers.Length, XMLString(FieldType.StringQualifiers.AllowedLength)));
                            ElsIf ItemType = Type("Number") Then
                                TypeNames.Add("Number");
                                Field.Insert("number", New Structure("digits, fractionDigits, allowedSign",
                                    FieldType.NumberQualifiers.Digits, FieldType.NumberQualifiers.FractionDigits, XMLString(FieldType.NumberQualifiers.AllowedSign)));
                            ElsIf ItemType = Type("Date") Then
                                TypeNames.Add("Date");
                                Field.Insert("date", New Structure("dateFractions", XMLString(FieldType.DateQualifiers.DateFractions)));
                            ElsIf ItemType = Type("Boolean") Then
                                TypeNames.Add("Boolean");
                            ElsIf ItemType = Type("ValueStorage") Then
                                TypeNames.Add("ValueStorage");
                            ElsIf ItemType = Type("UUID") Then
                                TypeNames.Add("UUID");
                            Else
                                TypeObject = Metadata.FindByType(ItemType);

                                If TypeObject = Undefined Then
                                    TypeNames.Add(String(ItemType));
                                Else
                                    TypeNames.Add(TypeObject.FullName());
                                EndIf;
                            EndIf;
                        EndDo;

                        Field.Insert("types", TypeNames);
                        Item.fields.Add(Field);
                    EndDo;

                    If PageCategory = "Enums" Then
                        For Each EnumValue In MetadataObject.EnumValues Do
                            Item.values.Add(New Structure("name, synonym", EnumValue.Name, EnumValue.Synonym));
                        EndDo;
                    EndIf;
                Except
                    Item.error = ErrorDescription();
                EndTry;
            EndDo;
        EndIf;

        If Asked = "common" Or (Asked = "page" And Parameters["storage"] = True And MetadataObjects.Count() > 0) Then
            Try
                If Asked = "page" Then
                    For Each Entry In ByFullName Do
                        Entry.Value.storage = New Array;
                    EndDo;

                    Table = GetDBStorageStructureInfo(MetadataObjects, True);
                Else
                    Table = GetDBStorageStructureInfo(Undefined, True);
                EndIf;

                If Table.Columns.Find("StorageTableName") <> Undefined Then
                    StorageTableColumn = "StorageTableName";
                    TableNameColumn = "TableName";
                    MetadataColumn = "Metadata";
                    PurposeColumn = "Purpose";
                    FieldsColumn = "Fields";
                    IndexesColumn = "Indexes";
                    StorageFieldColumn = "StorageFieldName";
                    FieldNameColumn = "FieldName";
                    StorageIndexColumn = "StorageIndexName";
                Else
                    StorageTableColumn = "ИмяТаблицыХранения";
                    TableNameColumn = "ИмяТаблицы";
                    MetadataColumn = "Метаданные";
                    PurposeColumn = "Назначение";
                    FieldsColumn = "Поля";
                    IndexesColumn = "Индексы";
                    StorageFieldColumn = "ИмяПоляХранения";
                    FieldNameColumn = "ИмяПоля";
                    StorageIndexColumn = "ИмяИндексаХранения";
                EndIf;

                For Each Row In Table Do
                    TableFields = New Array;

                    For Each FieldRow In Row[FieldsColumn] Do
                        TableFields.Add(New Structure("storageName, name, metadata",
                            FieldRow[StorageFieldColumn], FieldRow[FieldNameColumn], FieldRow[MetadataColumn]));
                    EndDo;

                    Indexes = New Array;

                    For Each IndexRow In Row[IndexesColumn] Do
                        IndexFields = New Array;

                        For Each IndexField In IndexRow[FieldsColumn] Do
                            IndexFields.Add(New Structure("storageName, name", IndexField[StorageFieldColumn], IndexField[FieldNameColumn]));
                        EndDo;

                        Indexes.Add(New Structure("storageName, fields", IndexRow[StorageIndexColumn], IndexFields));
                    EndDo;

                    MetadataName = Row[MetadataColumn];
                    StorageTable = New Structure("storageName, name, metadata, purpose, fields, indexes",
                        Row[StorageTableColumn], Row[TableNameColumn], MetadataName, Row[PurposeColumn], TableFields, Indexes);

                    OwnerName = MetadataName;
                    First = Find(MetadataName, ".");

                    If First > 0 Then
                        Second = Find(Mid(MetadataName, First + 1), ".");

                        If Second > 0 Then
                            OwnerName = Left(MetadataName, First + Second - 1);
                        EndIf;
                    EndIf;

                    If Asked = "page" Then
                        Owner = ByFullName.Get(OwnerName);

                        If Owner = Undefined Then
                            Raise "The storage table " + StorageTable.storageName + " belongs to " + MetadataName + ", which is no object of the page.";
                        EndIf;

                        Owner.storage.Add(StorageTable);
                    ElsIf OwnerName = "" Or FullNames.Get(OwnerName) = Undefined Then
                        Common.Add(StorageTable);
                    EndIf;
                EndDo;
            Except
                If Asked = "common" Then
                    Raise;
                EndIf;

                StorageError = ErrorDescription();

                For Each Item In Objects Do
                    Item.storage = Undefined;
                EndDo;
            EndTry;
        EndIf;

        If Asked = "census" Then
            Info = New SystemInfo;
            Result = New Structure("platform, configuration, categories",
                Info.AppVersion,
                New Structure("name, version, compatibilityMode", Metadata.Name, Metadata.Version, XMLString(Metadata.CompatibilityMode)),
                Counts);
        ElsIf Asked = "page" Then
            Result = New Structure("objects, storageError", Objects, StorageError);
        Else
            Result = Common;
        EndIf;
        """;
}
