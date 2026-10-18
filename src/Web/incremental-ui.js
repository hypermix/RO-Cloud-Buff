  // Mounted regions own their DOM. Keys retain identity across edits and reordering.
  const renderRegions=new WeakMap(),cardRenderVersions=new Map(),markupCache=new Map();
  const renderStats={mounts:0,buffFlushes:0,buffRows:0,discoveryFlushes:0,discoveryRows:0,regions:0,rowTemplates:0,cards:0,skippedCards:0,added:0,removed:0,moved:0,attributes:0,text:0,totalMs:0};
  window.roRenderDiagnostics={snapshot:()=>({...renderStats}),reset:()=>{for(const key of Object.keys(renderStats))renderStats[key]=0;}};
  let renderedPage=null;
  function nativeMemoMarkup(scope,id,version,create){const key=scope+':'+id,previous=markupCache.get(key);if(previous?.version===version)return previous.html;const html=create();markupCache.set(key,{version,html});renderStats.rowTemplates++;return html;}
  function renderFragment(html){const template=document.createElement('template');template.innerHTML=html;globalThis.lucide?.createIcons({root:template.content,attrs:{width:16,height:16}});return template.content;}
  function renderKey(node){
   if(node.nodeType!==Node.ELEMENT_NODE)return null;
   if(node.hasAttribute('data-storage-card'))return 'stored:'+node.dataset.storageCard;
   if(node.hasAttribute('data-character'))return 'card:'+node.dataset.character;
   if(node.matches('.rb-managed-entry,.rb-managed-dictionary'))return node.classList[0]+':'+node.querySelector('[data-manage-select]')?.dataset.manageSelect;
   if(node.matches('.rb-buff'))return 'buff:'+node.dataset.card+':'+node.dataset.id;
   if(node.hasAttribute('data-toggle-buff'))return 'choice:'+node.dataset.toggleBuff;
   if(node.matches('label')&&node.querySelector('[data-toggle-buff]'))return 'choice-label:'+node.querySelector('[data-toggle-buff]').dataset.toggleBuff;
   if(node.id)return 'id:'+node.id;
   if(node.hasAttribute('data-action'))return 'action:'+node.dataset.action+':'+(node.dataset.card||'')+':'+(node.dataset.id||node.dataset.page||node.dataset.arrangement||node.dataset.buffLayout||node.dataset.token||node.dataset.actor||node.dataset.step||'');
   for(const attr of ['data-input','data-control','data-search','data-search-empty','data-native-slot'])if(node.hasAttribute(attr))return attr+':'+node.getAttribute(attr);
   if(node.hasAttribute('data-lucide'))return 'icon:'+node.getAttribute('data-lucide');
   return node.classList.length?'class:'+node.classList[0]:null;
  }
  function renderCompatible(a,b){return a.nodeType===b.nodeType&&(a.nodeType!==Node.ELEMENT_NODE||(a.localName===b.localName&&a.namespaceURI===b.namespaceURI));}
  function renderMove(parent,node,before){if(node===before)return;if(parent.moveBefore&&node.isConnected&&parent.isConnected)parent.moveBefore(node,before);else parent.insertBefore(node,before);renderStats.moved++;}
  function renderChildren(parent,desired,options={}){
   const old=[...parent.childNodes],nextNodes=[...desired.childNodes],oldCounts=new Map(),newCounts=new Map();
   for(const node of old){const key=renderKey(node);if(key)oldCounts.set(key,(oldCounts.get(key)||0)+1);}
   for(const node of nextNodes){const key=renderKey(node);if(key)newCounts.set(key,(newCounts.get(key)||0)+1);}
   const stableKey=node=>{const key=renderKey(node);return key&&(oldCounts.get(key)||0)<=1&&(newCounts.get(key)||0)<=1?key:null;};
   const keyed=new Map(old.map(n=>[stableKey(n),n]).filter(([key])=>key)),used=new Set();let anchor=parent.firstChild;
   for(const next of nextNodes){
    const key=stableKey(next);let current=key?keyed.get(key):null;
    if(current&&(!renderCompatible(current,next)||used.has(current)))current=null;
    if(!current&&!key){current=old.find(n=>!used.has(n)&&!stableKey(n)&&renderKey(n)===renderKey(next)&&renderCompatible(n,next));}
    if(!current){current=next.cloneNode(true);parent.insertBefore(current,anchor);renderStats.added++;}
    else {if(current!==anchor)renderMove(parent,current,anchor);renderNode(current,next,options);}
    used.add(current);anchor=current.nextSibling;
   }
   for(const node of old)if(!used.has(node)){node.remove();renderStats.removed++;}
  }
  function renderNode(current,next,options={}){
   if(current.nodeType===Node.TEXT_NODE||current.nodeType===Node.COMMENT_NODE){if(current.nodeValue!==next.nodeValue){current.nodeValue=next.nodeValue;renderStats.text++;}return;}
   if(current.nodeType!==Node.ELEMENT_NODE)return;
   const valueChanged=current.getAttribute('value')!==next.getAttribute('value'),checkedChanged=current.hasAttribute('checked')!==next.hasAttribute('checked');
   for(const attr of [...current.attributes]){if(attr.name==='open'&&current.localName==='details')continue;if(!next.hasAttribute(attr.name)){current.removeAttribute(attr.name);renderStats.attributes++;}}
   for(const attr of next.attributes){if(attr.name==='open'&&current.localName==='details')continue;if(current.getAttribute(attr.name)!==attr.value){current.setAttribute(attr.name,attr.value);renderStats.attributes++;}}
   if(options.preserveBoard&&current.matches('.rb-cards'))return;
   // Uncontrolled drafts and user-expanded details survive unrelated region updates.
   if(current instanceof HTMLInputElement){if(valueChanged&&current.value!==next.value)current.value=next.value;if(checkedChanged)current.checked=next.checked;return;}
   if(current instanceof HTMLTextAreaElement){if(current.defaultValue!==next.defaultValue){current.defaultValue=next.defaultValue;current.value=next.value;}return;}
   const selectValue=current instanceof HTMLSelectElement?next.value:null;
   const selectChanged=selectValue!==null&&current.querySelector('option[selected]')?.value!==next.querySelector('option[selected]')?.value;
   renderChildren(current,next,options);
   if(selectChanged&&current.value!==selectValue)current.value=selectValue;
  }
  function renderRegion(node,version,html,options={}){
   if(renderRegions.get(node)===version)return false;
   const focused=document.activeElement,selection=focused instanceof HTMLInputElement?{start:focused.selectionStart,end:focused.selectionEnd,direction:focused.selectionDirection}:null;
   renderChildren(node,renderFragment(html()),options);renderRegions.set(node,version);renderStats.regions++;
   if(focused?.isConnected&&document.activeElement!==focused){focused.focus({preventScroll:true});if(selection?.start!=null)focused.setSelectionRange(selection.start,selection.end,selection.direction);}
   return true;
  }
  function renderCardVersion(c){return JSON.stringify([c.id,c.name,c.icon,c.connected,c.collapsed,c.mode,c.pinRequired,c.buffLayout,!c.connected?[c.connectionText,c.captureStats,c.observedCandidates,c.selectedActor,c.candidate]:null,state.menu===c.id,nativeBoardCards()[0]?.id===c.id,nativeBoardCards().at(-1)?.id===c.id,c.buffs.map(b=>[b.id,b.hidden,b.remember,b.alert,b.warning,b.order,b.pinOrder,b.presence,b.left>0&&b.left<=10,def(b.id)])]);}
  function renderCards(page){
   renderRegion(page,JSON.stringify([nativeBoardCards().map(c=>c.id),state.cards.length,state.arrangement,state.volume,count()]),()=>cardsPage(()=>''),{preserveBoard:true});
   const board=page.querySelector('.rb-cards'),add=board.querySelector('.rb-add-client'),existing=new Map([...board.querySelectorAll(':scope>.rb-character')].map(n=>[n.dataset.character,n]));
   let anchor=board.firstElementChild;
   for(const c of nativeBoardCards()){let node=existing.get(c.id);const version=renderCardVersion(c);if(!node){node=renderFragment(cardView(c)).firstElementChild;board.insertBefore(node,anchor);renderStats.added++;cardRenderVersions.set(c.id,version);renderStats.cards++;}
    else {if(node!==anchor)renderMove(board,node,anchor);if(cardRenderVersions.get(c.id)!==version){renderNode(node,renderFragment(cardView(c)).firstElementChild);cardRenderVersions.set(c.id,version);renderStats.cards++;}else renderStats.skippedCards++;}
    anchor=node.nextElementSibling;existing.delete(c.id);
   }
   for(const [id,node] of existing){node.remove();cardRenderVersions.delete(id);renderStats.removed++;}
   if(add&&board.lastElementChild!==add)renderMove(board,add,null);const empty=board.querySelector('[data-board-empty]');if(!nativeBoardCards().length&&!empty)board.append(renderFragment('<div class="rb-board-empty" data-board-empty><strong>필요한 캐릭터를 꺼내보세요</strong><p>보관함에서 모니터에 표시할 카드를 선택하세요.</p><button type="button" class="rb-secondary cursor-interaction" data-action="toggle-storage">캐릭터 보관함 열기</button></div>'));else if(nativeBoardCards().length)empty?.remove();
  }
  function nativeSelectionModal(){const c=state.cards.find(c=>c.id===state.modal.card);if(!c)return '';return `<div class="rb-overlay"><section class="rb-dialog rb-selection-dialog" role="dialog" aria-modal="true" aria-label="${esc(c.name)} 표시할 버프"><header class="rb-selection-head"><div class="rb-dialog-top"><div><h3>${esc(c.name)} · 표시할 버프</h3><p>모니터에 표시할 버프를 선택하세요.</p></div><button type="button" data-action="close" class="rb-icon-button cursor-interaction" aria-label="닫기">${icon('x')}</button></div>${searchField('selection')}</header><div class="rb-dictionary rb-selection-list">${state.definitions.map(d=>{const b=c.buffs.find(b=>b.id===d.id);return `<label class="rb-check" data-search-scope="selection" data-search-text="${esc(d.name)} ${d.id}"><input type="checkbox" data-toggle-buff="${d.id}" ${b&&!b.hidden?'checked':''}><span>${esc(d.name)}${d.kind==='potion'?' · 물약':''}</span></label>`;}).join('')}<div class="rb-selection-empty" data-search-empty="selection" hidden>검색 결과가 없습니다.</div></div><footer class="rb-selection-footer"><span data-selection-count></span><div><button type="button" class="cursor-interaction" data-action="close">취소</button><button type="button" class="rb-primary cursor-interaction" data-action="save-card">적용</button></div></footer></section></div>`;}
  function nativeSelectionCount(){const node=app.querySelector('[data-selection-count]');if(node){const text='선택한 버프 '+app.querySelectorAll('[data-toggle-buff]:checked').length+'개';if(node.textContent!==text)node.textContent=text;}}
  app.addEventListener('change',event=>{if(event.target.matches('[data-toggle-buff]'))nativeSelectionCount();});
  function render(){
   const started=performance.now();
   if(nativeDeferRender())return;
   if(!app.querySelector(':scope>.rb-body')){
    const navs=[['cards','캐릭터','panels-top-left'],['dictionary','버프 사전','book-open'],['discover','새로 발견','scan-line'],['settings','설정','sliders-horizontal']];
    app.append(renderFragment(`<div class="rb-body"><nav class="rb-sidebar" aria-label="메인 메뉴"><div class="rb-sidebar-label">워크스페이스</div><button type="button" class="rb-nav rb-storage-toggle cursor-interaction" data-action="toggle-storage" aria-label="캐릭터 보관함" aria-expanded="false" data-tooltip="캐릭터 보관함">${icon('archive')}<span class="rb-nav-label">캐릭터 보관함</span><span class="rb-storage-indicator" hidden></span></button>${navs.map(([key,name,ico])=>`<button type="button" class="rb-nav cursor-interaction" data-action="page" data-page="${key}" aria-label="${name}">${icon(ico)}<span class="rb-nav-label">${name}</span>${key==='discover'?'<span class="rb-nav-count">0</span>':''}</button>`).join('')}<div class="rb-sidebar-bottom"><span class="rb-online"><span class="rb-dot"></span>로컬 저장</span><div style="margin-top:5px">나만의 버프 사전</div></div></nav><main class="rb-main"><div class="rb-page-scroll"></div></main></div><footer class="rb-app-footer"><span>v${window.roAppVersion}</span><span data-native-summary></span></footer><div data-native-slot="storage" style="display:contents"></div><div data-native-slot="modal" style="display:contents"></div><div data-native-slot="notice" style="display:contents"></div>`));renderStats.mounts++;
   }
   app.dataset.view='board';
   for(const node of app.querySelectorAll('.rb-nav')){const selected=node.dataset.page===state.page;if(selected&&node.getAttribute('aria-current')!=='page')node.setAttribute('aria-current','page');else if(!selected&&node.hasAttribute('aria-current'))node.removeAttribute('aria-current');}
   nativePaintNav();const summary=app.querySelector('[data-native-summary]'),summaryText=`추적 중 ${state.cards.filter(c=>c.connected).length}/${state.cards.length}`;if(summary.textContent!==summaryText)summary.textContent=summaryText;
   const page=app.querySelector('.rb-page-scroll');
   if(renderedPage!==state.page){page.replaceChildren();renderRegions.delete(page);cardRenderVersions.clear();renderedPage=state.page;markupCache.clear();page.scrollTop=0;}
   nativeRenderStorage();
   if(state.page==='cards')renderCards(page);
   else if(state.page==='dictionary')renderRegion(page,JSON.stringify([state.definitions,[...managementSelected],managementMessage,!!managementUndo,managementDelete]),dictionaryPage);
   else if(state.page==='discover')renderRegion(page,JSON.stringify([state.discoveries,state.ignoredIds,state.ignoredEntries,state.cards.map(c=>[c.id,c.name]),managementSort,managementIgnored,managementEdit,[...managementSelected],managementMessage,!!managementUndo]),discoveryPage);
   else renderRegion(page,JSON.stringify([state.volume,state.sound,state.pin,alertHistoryRevision]),()=>settingsPage()+nativeAlertHistoryPanel());
   const modalKey=state.modal?`${state.modal.type}:${state.modal.id||''}:${state.modal.card||''}`:'',focusModal=!!modalKey&&modalKey!==lastModalKey;lastModalKey=modalKey;
   renderRegion(app.querySelector('[data-native-slot="modal"]'),JSON.stringify([state.modal,state.modal?state.definitions:null,state.modal?.card?state.cards.find(c=>c.id===state.modal.card)?.buffs:null]),()=>state.modal?.type==='create-profile'?nativeStorageModal():state.modal?.type==='card-settings'?nativeSelectionModal():modal());
   renderRegion(app.querySelector('[data-native-slot="notice"]'),JSON.stringify([state.notice,!!undoIgnore]),()=>state.notice?`<div class="rb-bottom-status" role="status" aria-live="polite">${esc(state.notice)}${undoIgnore?'<button type="button" class="cursor-interaction" data-action="undo-ignore">실행 취소</button>':''}</div>`:'');
   for(const menu of app.querySelectorAll(':scope>.rb-menu'))if(!state.menu||menu.querySelector('[data-card]')?.dataset.card!==state.menu)menu.remove();
   nativeSelectionCount();positionCardMenu();filterSearch('dictionary');filterSearch('selection');filterSearch('discover');managementPaintSelection();
   if(focusModal)queueMicrotask(()=>app.querySelector('.rb-dialog input[type="text"],.rb-dialog input[type="number"],.rb-dialog button')?.focus());
   renderStats.totalMs+=performance.now()-started;
  }
