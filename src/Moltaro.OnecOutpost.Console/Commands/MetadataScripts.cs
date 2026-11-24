namespace Moltaro.OnecOutpost.Console.Commands;

// The one script that metadata runs in a base as Reads, as CheckScripts holds check's, in the English variant of the 1C
// language and within what the compatibility mode 8.2.13 allows, without a procedure, Find and Mid where a later mode
// has StrFind and StrSplit. Its parameter read chooses what it reads: census, the configuration and the count of each of
// its fifteen categories; page, the objects of a category from an index with their fields and, when asked, their
// storage by GetDBStorageStructureInfo with the DBMS's names; common, the storage tables of no object of those
// categories. The S10 brief, "Internal of S10c", gives its outline and the form of what it gives:
// - census: {"platform", "configuration": {"name", "version", "compatibilityMode"}, "categories": [{"category",
//   "count"}]}, the fifteen categories in the report's order; an unreadable census value is {"error": "..."}.
// - page, with category, from, count and storage: {"objects": [...], "storageError"}, each object with name, fullName,
//   synonym, comment, properties, fields, values, storage and error. Each property is read on its own, so the object
//   keeps the readable values and its error names each failed property. Storage is its tables when storage is true
//   and null otherwise, and storageError the raise of the storage,
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

            // The report's keys and the metadata properties of this kind.
            PropertyNames = New Structure;

            If PageCategory = "Catalogs" Then
                PropertyNames = New Structure("hierarchical, hierarchyType, codeLength, descriptionLength, owners",
                    "Hierarchical", "HierarchyType", "CodeLength", "DescriptionLength", "Owners");
            ElsIf PageCategory = "Documents" Then
                PropertyNames = New Structure("numberLength, numberType, numberPeriodicity, posting, registerRecords",
                    "NumberLength", "NumberType", "NumberPeriodicity", "Posting", "RegisterRecords");
            ElsIf PageCategory = "InformationRegisters" Then
                PropertyNames = New Structure("periodicity, writeMode", "InformationRegisterPeriodicity", "WriteMode");
            ElsIf PageCategory = "AccumulationRegisters" Then
                PropertyNames = New Structure("registerType", "RegisterType");
            ElsIf PageCategory = "ChartsOfCharacteristicTypes" Or PageCategory = "ChartsOfAccounts" Or PageCategory = "ChartsOfCalculationTypes" Or PageCategory = "ExchangePlans" Then
                PropertyNames = New Structure("codeLength, descriptionLength", "CodeLength", "DescriptionLength");
            ElsIf PageCategory = "BusinessProcesses" Then
                PropertyNames = New Structure("numberLength", "NumberLength");
            ElsIf PageCategory = "Tasks" Then
                PropertyNames = New Structure("numberLength, descriptionLength", "NumberLength", "DescriptionLength");
            EndIf;

            Last = Min(Parameters["from"] + Parameters["count"], PageCollection.Count()) - 1;

            For Index = Parameters["from"] To Last Do
                Item = New Structure("name, fullName, synonym, comment, properties, fields, values, storage, error");
                Item.properties = New Structure;
                Item.fields = New Array;
                Item.values = New Array;
                Objects.Add(Item);
                Errors = New Array;

                Try
                    MetadataObject = PageCollection.Get(Index);
                    MetadataObjects.Add(MetadataObject);
                Except
                    Item.error = "object: " + ErrorDescription();
                    Continue;
                EndTry;

                Try
                    Item.name = MetadataObject.Name;
                Except
                    Errors.Add("name: " + ErrorDescription());
                EndTry;

                Try
                    Item.fullName = MetadataObject.FullName();
                    ByFullName.Insert(Item.fullName, Item);
                Except
                    Errors.Add("fullName: " + ErrorDescription());
                EndTry;

                Try
                    Item.synonym = MetadataObject.Synonym;
                Except
                    Errors.Add("synonym: " + ErrorDescription());
                EndTry;

                Try
                    Item.comment = MetadataObject.Comment;
                Except
                    Errors.Add("comment: " + ErrorDescription());
                EndTry;

                For Each Property In PropertyNames Do
                    Try
                        PropertyValue = MetadataObject[Property.Value];

                        If Property.Key = "owners" Or Property.Key = "registerRecords" Then
                            Names = New Array;
                            Item.properties.Insert(Property.Key, Names);

                            For Each Listed In PropertyValue Do
                                Try
                                    Names.Add(Listed.FullName());
                                Except
                                    Errors.Add(Property.Key + ": " + ErrorDescription());
                                EndTry;
                            EndDo;
                        ElsIf TypeOf(PropertyValue) = Type("Number") Or TypeOf(PropertyValue) = Type("Boolean") Then
                            Item.properties.Insert(Property.Key, PropertyValue);
                        Else
                            Item.properties.Insert(Property.Key, XMLString(PropertyValue));
                        EndIf;
                    Except
                        Errors.Add(Property.Key + ": " + ErrorDescription());
                    EndTry;
                EndDo;

                Sources = New Array;
                Rest = FieldCollections + ",";

                While Rest <> "" Do
                    Comma = Find(Rest, ",");
                    CollectionName = Left(Rest, Comma - 1);
                    Rest = Mid(Rest, Comma + 1);

                    Try
                        If CollectionName = "Value" Then
                            Sources.Add(New Structure("collection, tabularSection, member", "Value", Undefined, MetadataObject));
                        ElsIf CollectionName = "TabularSections" Then
                            For Each Section In MetadataObject.TabularSections Do
                                SectionName = Undefined;

                                Try
                                    SectionName = Section.Name;
                                Except
                                    Errors.Add("TabularSections.name: " + ErrorDescription());
                                EndTry;

                                Try
                                    For Each Member In Section.Attributes Do
                                        Sources.Add(New Structure("collection, tabularSection, member", "Attributes", SectionName, Member));
                                    EndDo;
                                Except
                                    Errors.Add("TabularSections.attributes: " + ErrorDescription());
                                EndTry;
                            EndDo;
                        ElsIf CollectionName <> "" Then
                            For Each Member In MetadataObject[CollectionName] Do
                                Sources.Add(New Structure("collection, tabularSection, member", CollectionName, Undefined, Member));
                            EndDo;
                        EndIf;
                    Except
                        Errors.Add(CollectionName + ": " + ErrorDescription());
                    EndTry;
                EndDo;

                For Each Source In Sources Do
                    Member = Source.member;
                    Field = New Structure("collection, tabularSection, name, synonym, indexing, types, string, number, date",
                        Source.collection, Source.tabularSection);
                    Item.fields.Add(Field);
                    FieldPath = Source.collection;

                    If Source.tabularSection <> Undefined Then
                        FieldPath = "TabularSections." + Source.tabularSection + "." + FieldPath;
                    EndIf;

                    Try
                        If Source.collection = "Value" Then
                            Field.name = "Value";
                        Else
                            Field.name = Member.Name;
                        EndIf;

                        FieldPath = FieldPath + "." + Field.name;
                    Except
                        Errors.Add(FieldPath + ".name: " + ErrorDescription());
                    EndTry;

                    Try
                        Field.synonym = Member.Synonym;
                    Except
                        Errors.Add(FieldPath + ".synonym: " + ErrorDescription());
                    EndTry;

                    Indexing = Undefined;

                    Try
                        Indexing = Member.Indexing;
                    Except
                        // A field without an Indexing property has no indexing to convert.
                    EndTry;

                    If Indexing <> Undefined Then
                        Try
                            Field.indexing = XMLString(Indexing);
                        Except
                            Errors.Add(FieldPath + ".indexing: " + ErrorDescription());
                        EndTry;
                    EndIf;

                    Try
                        FieldType = Member.Type;
                        ItemTypes = FieldType.Types();
                        Field.types = New Array;
                    Except
                        Errors.Add(FieldPath + ".types: " + ErrorDescription());
                        Continue;
                    EndTry;

                    For Each ItemType In ItemTypes Do
                        If ItemType = Type("String") Then
                            Field.types.Add("String");
                            StringInfo = New Structure("length, allowedLength");
                            Field.Insert("string", StringInfo);

                            Try
                                StringInfo.length = FieldType.StringQualifiers.Length;
                            Except
                                Errors.Add(FieldPath + ".string.length: " + ErrorDescription());
                            EndTry;

                            Try
                                StringInfo.allowedLength = XMLString(FieldType.StringQualifiers.AllowedLength);
                            Except
                                Errors.Add(FieldPath + ".string.allowedLength: " + ErrorDescription());
                            EndTry;
                        ElsIf ItemType = Type("Number") Then
                            Field.types.Add("Number");
                            NumberInfo = New Structure("digits, fractionDigits, allowedSign");
                            Field.Insert("number", NumberInfo);

                            Try
                                NumberInfo.digits = FieldType.NumberQualifiers.Digits;
                            Except
                                Errors.Add(FieldPath + ".number.digits: " + ErrorDescription());
                            EndTry;

                            Try
                                NumberInfo.fractionDigits = FieldType.NumberQualifiers.FractionDigits;
                            Except
                                Errors.Add(FieldPath + ".number.fractionDigits: " + ErrorDescription());
                            EndTry;

                            Try
                                NumberInfo.allowedSign = XMLString(FieldType.NumberQualifiers.AllowedSign);
                            Except
                                Errors.Add(FieldPath + ".number.allowedSign: " + ErrorDescription());
                            EndTry;
                        ElsIf ItemType = Type("Date") Then
                            Field.types.Add("Date");

                            Try
                                Field.Insert("date", New Structure("dateFractions", XMLString(FieldType.DateQualifiers.DateFractions)));
                            Except
                                Errors.Add(FieldPath + ".date.dateFractions: " + ErrorDescription());
                            EndTry;
                        ElsIf ItemType = Type("Boolean") Then
                            Field.types.Add("Boolean");
                        ElsIf ItemType = Type("ValueStorage") Then
                            Field.types.Add("ValueStorage");
                        ElsIf ItemType = Type("UUID") Then
                            Field.types.Add("UUID");
                        Else
                            Try
                                TypeObject = Metadata.FindByType(ItemType);

                                If TypeObject = Undefined Then
                                    Field.types.Add(String(ItemType));
                                Else
                                    Field.types.Add(TypeObject.FullName());
                                EndIf;
                            Except
                                Errors.Add(FieldPath + ".types: " + ErrorDescription());
                            EndTry;
                        EndIf;
                    EndDo;
                EndDo;

                If PageCategory = "Enums" Then
                    Try
                        For Each EnumValue In MetadataObject.EnumValues Do
                            Value = New Structure("name, synonym");
                            Item.values.Add(Value);

                            Try
                                Value.name = EnumValue.Name;
                            Except
                                Errors.Add("values.name: " + ErrorDescription());
                            EndTry;

                            Try
                                Value.synonym = EnumValue.Synonym;
                            Except
                                Errors.Add("values.synonym: " + ErrorDescription());
                            EndTry;
                        EndDo;
                    Except
                        Errors.Add("values: " + ErrorDescription());
                    EndTry;
                EndIf;

                For Each Failure In Errors Do
                    If Item.error = Undefined Then
                        Item.error = Failure;
                    Else
                        Item.error = Item.error + "; " + Failure;
                    EndIf;
                EndDo;
            EndDo;
        EndIf;

        If Asked = "common" Or (Asked = "page" And Parameters["storage"] = True) Then
            Try
                If Asked = "page" Then
                    If MetadataObjects.Count() = 0 Then
                        Raise "No metadata object was available for the storage inspection.";
                    EndIf;

                    // The storage API can inspect the object even when its full name is unreadable.
                    Table = GetDBStorageStructureInfo(MetadataObjects, True);

                    If ByFullName.Count() = 0 Then
                        Raise "No metadata object had a readable full name for matching its storage tables.";
                    EndIf;

                    For Each Entry In ByFullName Do
                        Entry.Value.storage = New Array;
                    EndDo;
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
                            Raise "The storage table " + StorageTable.storageName + " belongs to " + MetadataName + ", which matches no readable full name on the page.";
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
            Configuration = New Structure("name, version, compatibilityMode");
            Result = New Structure("platform, configuration, categories", Undefined, Configuration, Counts);

            Try
                Info = New SystemInfo;
                Result.platform = Info.AppVersion;
            Except
                Result.platform = New Structure("error", ErrorDescription());
            EndTry;

            Try
                Configuration.name = Metadata.Name;
            Except
                Configuration.name = New Structure("error", ErrorDescription());
            EndTry;

            Try
                Configuration.version = Metadata.Version;
            Except
                Configuration.version = New Structure("error", ErrorDescription());
            EndTry;

            Try
                Configuration.compatibilityMode = XMLString(Metadata.CompatibilityMode);
            Except
                Configuration.compatibilityMode = New Structure("error", ErrorDescription());
            EndTry;
        ElsIf Asked = "page" Then
            Result = New Structure("objects, storageError", Objects, StorageError);
        Else
            Result = Common;
        EndIf;
        """;
}
