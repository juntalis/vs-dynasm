------------------------------------------------------------------------------
-- Simple extensions to DynASM's preprocessing functionality.
--
-- This module makes a few small tweaks to DynASM and adds a handful of
-- utility directives to the preprocessor. (most of which centers on the
-- added ability to emit conditional C code based on its current environment)
-- NOTE: Unbeknownst to me, DynASM apparently already (mostly) supported the
--       directives with the '||' line prefix. In my defense, taking the time
--       to read things is clearly a job left up to nerds with protractors.
-- 
-- Currently added:
--
--   - New Instructions:
--     | pusha
--     | popa
--     | pushad -> pusha (only on 32-bit)
--     | pushaq -> pusha (only on 64-bit)
--     | popad  -> popa  (only on 32-bit)
--     | popaq  -> popa  (only on 64-bit)
--   
--   - New Directives:
--     | .cemit  - Emits a line of raw text to the output file (text is presumably C code)
--                 NOTE: The input line will be stripped of trailing semicolons, as well
--                       as leading and trailing whitespace prior to the line being
--                       written to the output.
--     | .cemitn - Same as .cemit, but adds a terminating semicolon to the end of the line.
------------------------------------------------------------------------------

-- Exported module table.
local _M = { _opt = nil, _arch = nil, _defs = nil, _ops = nil }

-- Cache library functions.
local type, tonumber, pairs, ipairs = type, tonumber, pairs, ipairs
local assert, unpack, setmetatable = assert, unpack or table.unpack, setmetatable
local _s = string
local sub, format, byte, char = _s.sub, _s.format, _s.byte, _s.char
local find, match, gmatch, gsub = _s.find, _s.match, _s.gmatch, _s.gsub
local concat, sort, tinsert = table.concat, table.sort, table.insert

-- Cached Flags
local b_isx64, b_usesx86

-- Inherited tables and callbacks.
local wline, werror, wfatal, wwarn, wflush

-- Predicate for determining if the current architecture supports x86 instructions.
local function isx64()
  if not b_isx64 then
    b_isx64 =_M._arch == 'x64'
  end
  return b_isx64
end

-- Predicate for determining if the current architecture supports x86 instructions.
local function usesx86()
  if not b_usesx86 then
    b_usesx86 =_M._arch == 'x86' or isx64()
  end
  return b_usesx86
end

-- Given an indexed table, (such as the params list passed to our directives) join
-- the table together into a comma separated string.
local function wjoin(params)
  local t_parts = {}
  for _, param in ipairs(params) do
    tinsert(t_parts, param)
  end
  return concat(t_parts, ', ')
end

-- Contributed pseudo-opcodes.
local map_baseops = {}
local map_x86ops  = {}

local function cemitl(params, suffix, needindent)
  local outline
  if not params then
    outline = ''
  else
    outline = wjoin(params) .. (suffix or '')
  end
  wflush()
  wline(outline, needindent)
end

-- Pseudo-opcode to emit a line of C code to our output file.
map_baseops['.cemit_*'] = function(params)
  cemitl(params, nil, false)
end

-- Pseudo-opcode to emit a line of C code to our output file.
map_baseops[".cemitn_*"] = function(params)
  cemitl(params, ';', false)
end

-- Psuedo operators for 32-bit instructions
map_x86ops['pusha_0'] = '60'
map_x86ops['pushad_0'] = not isx64() and '60'
map_x86ops['pushaq_0'] =     isx64() and '60'
map_x86ops['popa_0'] = '61'
map_x86ops['popad_0'] = not isx64() and '61'
map_x86ops['popaq_0'] =     isx64() and '61'

-- Extended and altered command line option handlers
local opts_extra_map = {}

function opts_extra_map.ccomment()
  _M._opt.comment = '/* |'
  _M._opt.endcomment = ' */'
end

function opts_extra_map.cppcomment()
  _M._opt.comment = '// |'
  _M._opt.endcomment = ''
end

-- Pass callbacks from/to the DynASM core.
function _M.passcb(wl, we, wft, ww, wfl)
  wline, werror, wfatal, wwarn, wflush = wl, we, wft, ww, wfl
end

-- Setup the arch-specific module.
function _M.setup(arch, opt, defs, ops)
  _M._arch, _M._opt, _M._defs, _M._ops = arch, opt, defs, ops
end

-- Merge the core maps and the arch-specific maps.
function _M.mergemaps(source, destination)
  local source_ops = source
  if not destination then
    destination = source_ops
    source_ops = map_baseops
  end
  if source_ops ~= nil then
    for id,op in pairs(source_ops) do
      local check = destination[id]
      if not check or check ~= op then
        destination[id] = op
      end
    end
  end
  return source, destination
end

function _M.mergeall(map_op, map_coreop, opt_map)
  _M.mergemaps(map_coreop)
  if usesx86 then
    _M.mergemaps(map_x86ops, map_op)
  end
  _M.mergemaps(opts_extra_map, opt_map)
  return map_op, map_coreop
end

return _M
