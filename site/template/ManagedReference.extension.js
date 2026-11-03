// The XML docs cite the library's specs for its maintainers, as "(1.x spec §20.5)" or "(§9.4)". Readers of the
// site can't look those up, so the API pages leave them out.
var specRef = /\s*\((?:1\.x spec |SPEC(?:-1\.x)?\.md )?§[^()]*\)/g;
var bareRef = /\s*\(?(?:1\.x spec |spec )§[\d.]+\)?/g;

function clean(value) {
  if (typeof value === "string") return value.replace(specRef, "").replace(bareRef, "");
  if (Array.isArray(value)) return value.map(clean);
  if (value && typeof value === "object") {
    for (var key in value) value[key] = clean(value[key]);
  }
  return value;
}

exports.preTransform = function (model) {
  return clean(model);
};

exports.postTransform = function (model) {
  return model;
};
