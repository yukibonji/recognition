// ----------------------------------------------------------------------------------------------
// Copyright 2015 Mårten Rånge
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------------------
module Html

open FSharp.Reflection

open System
open System.IO
open System.Net
open System.Text

module Common =
  type BinaryTree<'T> =
    | Empty
    | Leaf1  of 'T
    | Leaf2  of 'T*'T
    | Leaf3  of 'T*'T*'T
    | Leaf4  of 'T*'T*'T*'T
    | LeafN  of 'T []
    | Fork  of BinaryTree<'T>*BinaryTree<'T>

  let inline isEmpty t =
    match t with
    | Empty -> true
    | _     -> false

  let inline join (l : BinaryTree<'T>) (r : BinaryTree<'T>) : BinaryTree<'T> =
    match l, r with
    | Empty , _     -> r
    | _     , Empty -> l
    | _             -> Fork (l,r)

open Common

module Model =
  type HtmlLink           = HtmlLink      of string*string
  type HtmlMeta           = HtmlMeta      of string*string
  type HtmlTextInput      =
    | TextInput
    | RadioInput
    | SubmitInput
  type HtmlFormMethod     =
    | UseGet
    | UsePost
  type HtmlStyleRef       = HtmlStyleRef of string
  type HtmlStyleRefTree   = BinaryTree<HtmlStyleRef>
  type HtmlAttribute   =
    | Alt               of string
    | Action            of string
    | Href              of string
    | Id                of string
    | InputType         of HtmlTextInput
    | Class             of HtmlStyleRefTree
    | Method            of HtmlFormMethod
    | Name              of string
    | Src               of string
    | Value             of string
    | KeyValue          of string*string
    | KeyUnencodedValue of string*string
  type HtmlAttributeTree  = BinaryTree<HtmlAttribute>

  type HtmlGeneratorContext =
    {
      Name : string
    }

    static member Empty : HtmlGeneratorContext = {Name = ""}

  [<NoEquality>]
  [<NoComparison>]
  type HtmlElement =
    | Text            of string
    | Tag             of string*HtmlAttributeTree*(HtmlElement [])
    | ClosedTag       of string*HtmlAttributeTree
    | WithClass       of HtmlStyleRefTree*(HtmlElement [])
    | WithAttributes  of HtmlAttributeTree*(HtmlElement [])
    | Generated       of (HtmlGeneratorContext -> int -> HtmlStyleRefTree -> HtmlAttributeTree -> (int -> string -> unit) -> unit)

  [<NoEquality>]
  [<NoComparison>]
  type HtmlPage =
    {
      Title           : string
      Links           : HtmlLink []
      Metas           : HtmlMeta []
      Body            : HtmlElement []
    }

open Model

let inline styleRef s                             = HtmlStyleRef s
let inline attribute k v                          = KeyValue (k,v)
let inline unencodedAttribute k v                 = KeyUnencodedValue (k,v)
let inline tag tag_ attributes elements           = Tag (tag_, attributes, elements)
let inline closedTag tag_ attributes              = ClosedTag (tag_, attributes)
let inline text txt                               = Text txt
let inline image src alt                          = closedTag "img" (Leaf2 (Src src, Alt alt))
let inline header1 elements                       = tag "h1" Empty elements
let inline header2 elements                       = tag "h2" Empty elements
let inline header3 elements                       = tag "h3" Empty elements
let lineBreak                                     = closedTag "br" Empty
let inline form action meth elements              = tag "form" (Leaf2 (Action action, Method meth)) elements
let inline fieldSet elements                      = tag "fieldset" Empty elements
let inline inputField input name value            = closedTag "input" (Leaf3 (InputType input, Name name, Value value))
let inline textField name value                   = inputField TextInput name value
let inline radioField name value                  = inputField RadioInput name value
let inline submitField name value                 = inputField SubmitInput name value
let inline textHeader1 text                       = header1 [|Text text|]
let inline textHeader2 text                       = header2 [|Text text|]
let inline textHeader3 text                       = header3 [|Text text|]
let inline paragraph elements                     = Tag ("p", Empty, elements)
let inline anchor href elements                   = Tag ("a", Leaf1 (Href href), elements)
let inline textLink href description              = anchor href [|Text description|]
let inline imageLink href src alt                 = anchor href [|image src alt|]
let inline generated g                            = Generated g
let inline withClass cls elements                 = WithClass (LeafN cls, elements)
let inline withClass_ cls element                 = withClass cls ([|element|])
let inline withAttributes attributes elements     = WithAttributes (LeafN attributes, elements)
let inline withAttributes_ attributes element     = WithAttributes (LeafN attributes, [|element|])

let inline link rel href                          = HtmlLink (rel,href)
let inline stylesheet href                        = HtmlLink ("stylesheet",href)
let inline meta name content                      = HtmlMeta (name,content)
let inline viewport content                       = HtmlMeta ("viewport",content)
let inline page title links metas body : HtmlPage =
  {
    Title = title
    Links = links
    Metas = metas
    Body  = body
  }

module Generator =
  let generateHtml (page : HtmlPage) : string =
    let html  = StringBuilder 64

    let inline htmlEncode (s : string) : string = WebUtility.HtmlEncode s
    let inline urlEncode  (s : string) : string = s

    let inline emptyStr s         = String.IsNullOrEmpty s
    let inline nonEmptyStr s      = not (emptyStr s)
    let inline ch   (c : char)    = ignore <| html.Append c
    let inline str  (s : string)  = ignore <| html.Append s
    let inline indent (i : int)   = ignore <| html.Append (' ', i)
    let inline newl ()            = ignore <| html.AppendLine ()
    let inline prekv k            =
      ch ' '
      str k
      str @"="""
    let inline postkv ()          =
      ch '"'
    let inline kv k v             =
      // TODO: Report empty k?
      if nonEmptyStr v then
        prekv k
        str v
        postkv ()
    let inline hkv k v            =
      kv k (htmlEncode v)
    let inline ukv k v            =
      kv k (urlEncode v)

    let inline append (i : int) (l : string) : unit =
      indent i
      ignore <| html.AppendLine l

    let rec renderStyleRefs (tree : HtmlStyleRefTree) =
      // TODO: ref means a new object
      let first = ref false
      let rs (HtmlStyleRef sref) =
        if nonEmptyStr sref then
          if !first then
            ch ' '
            first := false
          str sref
      match tree with
      | Empty ->
        ()
      | Leaf1 sref0 ->
        rs sref0
      | Leaf2 (sref0, sref1) ->
        rs sref0
        rs sref1
      | Leaf3 (sref0, sref1, sref2) ->
        rs sref0
        rs sref1
        rs sref2
      | Leaf4 (sref0, sref1, sref2, sref3) ->
        rs sref0
        rs sref1
        rs sref2
        rs sref3
      | LeafN srefs ->
        for sref in srefs do
          rs sref
      | Fork (l,r) ->
        renderStyleRefs l
        renderStyleRefs r

    let ckv (c : HtmlStyleRefTree) =
      if not (isEmpty c) then
        prekv "class"
        renderStyleRefs c
        postkv ()

    let rec renderAttributes
      (tree     : HtmlAttributeTree)
      (srefTree : HtmlStyleRefTree ) : bool =
      // TODO: inline?
      let ra attr =
        match attr with
        | InputType   v   ->
          let i =
            match v with
            | TextInput   -> "text"
            | RadioInput  -> "radio"
            | SubmitInput -> "submit"
          kv "type" i
          false
        | Method v        ->
          let i =
            match v with
            | UseGet      -> "GET"
            | UsePost     -> "POST"
          kv "method" i
          false
        | Action  v       -> ukv "action" v; false
        | Href    v       -> ukv "href"   v; false
        | Src     v       -> ukv "src"    v; false
        | Value   v       -> hkv "value"  v; false
        | Alt     v       -> hkv "alt"    v; false
        | Name    v       -> kv "name"    v; false
        | Id      v       -> kv "id"      v; false
        | Class c ->
          ckv (join c srefTree)
          true
        | KeyValue (k,v) when nonEmptyStr k ->
          hkv k v
          false
        | KeyValue (_, _) ->
          false  // TODO: Raise?
        | KeyUnencodedValue (k,v) when nonEmptyStr k ->
          kv k v
          false
        | KeyUnencodedValue (_, _) ->
          false  // TODO: Raise?

      match tree with
      | Empty ->
        false
      | Leaf1 attr0 ->
        let f0 = ra attr0
        f0
      | Leaf2 (attr0, attr1) ->
        let f0 = ra attr0
        let f1 = ra attr1
        f0 || f1
      | Leaf3 (attr0, attr1, attr2) ->
        let f0 = ra attr0
        let f1 = ra attr1
        let f2 = ra attr2
        f0 || f1 || f2
      | Leaf4 (attr0, attr1, attr2, attr3) ->
        let f0 = ra attr0
        let f1 = ra attr1
        let f2 = ra attr2
        let f3 = ra attr3
        f0 || f1 || f2 || f3
      | LeafN attrs ->
        let mutable hasClass = false
        for attr in attrs do
          hasClass <- ra attr || hasClass
        hasClass
      | Fork (l,r) ->
        let f0 = renderAttributes l srefTree
        let f1 = renderAttributes r srefTree
        f0 || f1

    let renderTag
      (closed     : bool              )
      (i          : int               )
      (tag        : string            )
      (attributes : HtmlAttributeTree )
      (srefTree   : HtmlStyleRefTree  ) =
      indent i
      ch '<'
      str tag
      let hasClass = renderAttributes attributes srefTree
      if not hasClass && not (isEmpty srefTree) then
        ckv srefTree
      if closed then
        ch '/'
      ch '>'
      newl ()

    let renderEndTag
      (i          : int               )
      (name       : string            ) =
      indent i
      str "</"
      str name
      ch '>'
      newl ()
    let inline renderClosedTag i tag attributes srefTree = renderTag true  i tag attributes srefTree
    let inline renderStartTag  i tag attributes srefTree = renderTag false i tag attributes srefTree

    let ctx = HtmlGeneratorContext.Empty

    let rec renderElements i cls attrs es =
      for e in es do
        match e with
        | Text text ->
          append i (htmlEncode text)
        | Tag (tag, attributes, ies) ->
          let ea = join attributes attrs
          if ies.Length > 0 then
            renderStartTag i tag ea cls
            renderElements (i + 2) Empty Empty ies
            renderEndTag i tag
          else
            renderClosedTag i tag ea cls
        | ClosedTag (tag, attributes) ->
          let ea = join attributes attrs
          renderClosedTag i tag ea cls
        | WithClass (newClass, inner) ->
          renderElements i newClass attrs inner
        | WithAttributes (newAttributes, inner) ->
          renderElements i cls newAttributes inner
        | Generated generator ->
          generator ctx i cls attrs append

    append 0 "<html>"
    append 2 "<head>"
    for (HtmlLink (rel, href)) in page.Links do
      append 4 (sprintf """<link rel="%s" href="%s"/>""" rel (urlEncode href))
    for (HtmlMeta (name, content)) in page.Metas do
      append 4 (sprintf """<meta name="%s" content="%s"/>""" name content)
    append 4 (sprintf "<title>%s</title>" (htmlEncode page.Title))
    append 2 "</head>"
    append 2 "<body>"
    renderElements 2 Empty Empty page.Body
    append 2 "</body>"
    append 0 "</html>"

    let result = html.ToString ()
  //  printfn "%s" result
    result
