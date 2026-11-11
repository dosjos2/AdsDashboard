// Add runnable read-only requests and assertions to the generated collection.
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '../postman/collections/azure-devops');
const definitions = {
 'list Discussion.request.yaml': {url:'{{baseUrl}}/{{project}}/_apis/wit/workItems/1/comments?api-version=7.1-preview.4&$top=100&$expand=renderedText&includeDeleted=false&order=asc', test:`pm.test('Discussion response has comments', () => pm.expect(pm.response.json().comments).to.be.an('array'));`},
 'resolve Identity.request.yaml': {url:'{{identityUrl}}/_apis/identities?api-version=7.1&searchFilter=General&filterValue={{email}}&queryMembership=None', test:`pm.test('Identity can be resolved', () => pm.expect(pm.response.json().value.length).to.be.above(0));\nif(pm.response.json().value[0]) pm.collectionVariables.set('myId', pm.response.json().value[0].id);`},
 'list Projects.request.yaml': {url:'{{baseUrl}}/_apis/projects?api-version=7.1&$top=100', test:`pm.test('Projects are an array', () => pm.expect(pm.response.json().value).to.be.an('array'));\nif(pm.response.json().value[0]) pm.collectionVariables.set('project', pm.response.json().value[0].id);`},
 'query Assigned Work.request.yaml': {url:'{{baseUrl}}/_apis/wit/wiql?api-version=7.1', body:{query:"SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = '{{email}}' AND [System.WorkItemType] IN ('Task', 'Feature', 'Bug', 'Product Backlog Item') ORDER BY [System.ChangedDate] DESC"}, test:`pm.test('WIQL returns references', () => pm.expect(pm.response.json().workItems).to.be.an('array'));\npm.collectionVariables.set('workIds', JSON.stringify(pm.response.json().workItems.slice(0, 2).map(w => w.id)));`},
 'read Work Item Details.request.yaml': {url:'{{baseUrl}}/_apis/wit/workitemsbatch?api-version=7.1', body:'{"ids": {{workIds}}, "errorPolicy": "Omit"}',test:`pm.test('Work details have fields', () => pm.response.json().value.forEach(w => {pm.expect(w.id).to.be.a('number'); pm.expect(w.fields['System.State']).to.be.a('string');}));`},
 'list Personal Pull Requests.request.yaml': {url:'{{baseUrl}}/{{project}}/_apis/git/pullrequests?api-version=7.1&searchCriteria.creatorId={{myId}}&searchCriteria.status=active&$top=100&$skip=0',test:`pm.test('PRs have creator and reviewers', () => pm.response.json().value.forEach(p => {pm.expect(p.createdBy.id).to.equal(pm.collectionVariables.get('myId')); pm.expect(p.reviewers).to.be.an('array');}));`},
 'list Recent Builds.request.yaml': {url:'{{baseUrl}}/{{project}}/_apis/build/builds?api-version=7.1&queryOrder=queueTimeDescending&minTime={{buildSince}}&$top=100',test:`pm.test('Builds have pipeline and status', () => pm.response.json().value.forEach(b => {pm.expect(b.definition.id).to.be.a('number'); pm.expect(b.status).to.be.a('string');}));`}
};
function visit(dir) {
 for (const entry of fs.readdirSync(dir,{withFileTypes:true})) {
  if (entry.name === '.resources') continue;
  const file = path.join(dir,entry.name);
  if (entry.isDirectory()) {visit(file);continue;}
  const d=definitions[entry.name]; if(!d) continue;
  const method=d.body ? 'POST':'GET';
  const body=d.body ? `body:\n  type: json\n  content: ${JSON.stringify(typeof d.body === 'string' ? d.body : JSON.stringify(d.body))}\n`:'';
  fs.writeFileSync(file, `$kind: http-request\nname: ${JSON.stringify(entry.name.replace('.request.yaml',''))}\norder: ${Object.keys(definitions).indexOf(entry.name)*1000+1000}\nmethod: ${method}\nurl: ${JSON.stringify(d.url)}\nheaders:\n  - {key: Accept, value: application/json}\n  - {key: Content-Type, value: application/json}\n${body}scripts:\n  - type: afterResponse\n    language: text/javascript\n    code: ${JSON.stringify(d.test)}\n`);
 }
}
visit(root);
const commentFile = path.join(root,'{project}-_apis/list Discussion.request.yaml');
const commentRequest = definitions['list Discussion.request.yaml'];
fs.writeFileSync(commentFile, `$kind: http-request\nname: List discussion\norder: 9000\nmethod: GET\nurl: ${JSON.stringify(commentRequest.url)}\nscripts:\n  - type: afterResponse\n    language: text/javascript\n    code: ${JSON.stringify(commentRequest.test)}\n`);
fs.mkdirSync(path.join(root,'.resources'),{recursive:true});
fs.writeFileSync(path.join(root,'.resources/definition.yaml'), `$kind: collection
name: Azure DevOps read checks
description: Generated from azure-devops.yaml; executable read-only checks. No production token is stored here.
variables:
  - {key: baseUrl, value: 'https://dev.azure.com/helsedir'}
  - {key: identityUrl, value: 'https://vssps.dev.azure.com/helsedir'}
  - {key: email, value: 'jan.ole.skotterud@helsedir.no'}
  - {key: basicAuthUsername, value: ''}
  - {key: basicAuthPassword, value: ''}
  - {key: myId, value: '11111111-1111-1111-1111-111111111111'}
  - {key: project, value: 'project-a'}
  - {key: workIds, value: '[1,3]'}
auth:
  type: basic
  credentials:
    - {key: username, value: '{{basicAuthUsername}}'}
    - {key: password, value: '{{basicAuthPassword}}'}
scripts:
  - type: http:beforeRequest
    language: text/javascript
    code: pm.collectionVariables.set('buildSince', new Date(Date.now() - 7 * 86400000).toISOString());
  - type: http:afterResponse
    language: text/javascript
    code: |
      pm.test('HTTP 200', () => pm.response.to.have.status(200));
      pm.test('JSON response', () => pm.expect(pm.response.json()).to.be.an('object'));
`);
